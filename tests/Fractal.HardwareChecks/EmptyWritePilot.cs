using System.Buffers.Binary;
using System.Text.Json;
using Fractal.Core;

// Deliberately restricted to an empty saved/live preset. Never part of normal tests or the production CLI.
static class EmptyWritePilot
{
    public static async Task<int> Run(int input, int output, DeviceDefinition d, string folder, Dictionary<string, string> options, bool recoverOnly = false, bool restoreOnly = false)
    {
        int slot = int.Parse(options["--preset"]), row = int.Parse(options["--row"]), column = int.Parse(options["--column"]);
        var scope = new EmptyPresetPilotScope(d, slot, row, column);
        string expectedHash = options["--expected-hash"];
        string backup = Path.GetFullPath(options["--backup"]);
        // Integrity-check the on-disk backup and its manifest before opening ports.
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(backup, "manifest.json")));
        var entries = manifest.RootElement.GetProperty("captures").EnumerateArray().Where(e => e.GetProperty("slot").GetInt32() == slot).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Backup must contain exactly one target entry.");
        string filename = entries[0].GetProperty("file").GetString()!;
        if (Path.GetFileName(filename) != filename) throw new InvalidDataException("Backup capture path must be local.");
        byte[] wire = File.ReadAllBytes(Path.Combine(backup, filename));
        SavedCapture baseline = SnapshotExport.DecodeFile(d, wire);
        if (PresetBody.Sha256(wire) != entries[0].GetProperty("sysex_sha256").GetString() ||
            PresetBody.Sha256(baseline.Image.RawImage) != entries[0].GetProperty("raw_sha256").GetString() ||
            PresetBody.Sha256(baseline.Image.Body) != entries[0].GetProperty("body_sha256").GetString() ||
            PresetBody.Sha256(baseline.Image.RawImage) != expectedHash || baseline.Image.Slot != slot)
            throw new InvalidDataException("Backup integrity/base hash mismatch.");
        var original = PresetBody.Decode(d, baseline.Image);
        if (original.Grid.Any(c => c.EffectId != 0) || original.SceneMidiPresent || original.Limitations.Length != 0)
            throw new InvalidDataException("Pilot requires a completely empty saved grid.");
        int defaultScene = BinaryPrimitives.ReadUInt16LittleEndian(baseline.Image.Body.AsSpan(0, 2)) + 1;
        if (defaultScene is < 1 or > 8) throw new InvalidDataException("Saved default-scene candidate is outside 1–8.");

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        CancellationToken token = cancellation.Token;
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        using var trace = new Trace(Path.Combine(folder, "trace.jsonl"));
        var results = new List<object>();
        DeviceContext? context = null; CurrentState? before = null, after = null;
        string stage = "preflight", lastSavedHash = expectedHash;
        string? failure = null; bool complete = false, restored = false;
        void Check(string name, bool passed)
        {
            results.Add(new { name, passed }); Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
            if (!passed) throw new InvalidDataException($"Pilot check failed: {name}");
        }
        try
        {
            await using var midi = new TracedMidi(await TransportConnection.OpenAsync(input, output, scope), trace);
            await using var session = new ReadSession(midi);
            context = await session.IdentifyAsync(d.Model, d.PresetCapacity, timeout, token);
            before = await session.CurrentAsync(context, timeout, token);
            Check("selected-authorized-target", before.Slot == slot && before.Name == baseline.Image.Name);
            var freshSaved = await session.SavedAsync(context, slot, timeout, token);
            string currentHash = restoreOnly ? options["--current-hash"] : expectedHash;
            Check("saved-base-hash", PresetBody.Sha256(freshSaved.Image.RawImage) == currentHash);
            if (restoreOnly)
            {
                // Explicit reconciliation of a retained partial pilot, never an automatic retry.
                string currentFolder = Path.GetFullPath(options["--current-backup"]);
                using var currentManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(currentFolder, "manifest.json")));
                var entry = currentManifest.RootElement.GetProperty("captures").EnumerateArray().Single(e => e.GetProperty("slot").GetInt32() == slot);
                string currentFile = entry.GetProperty("file").GetString()!;
                if (Path.GetFileName(currentFile) != currentFile) throw new InvalidDataException("Invalid partial capture path.");
                byte[] currentWire = File.ReadAllBytes(Path.Combine(currentFolder, currentFile));
                var retained = SnapshotExport.DecodeFile(d, currentWire);
                Check("partial-capture-integrity", PresetBody.Sha256(currentWire) == entry.GetProperty("sysex_sha256").GetString() &&
                    PresetBody.Sha256(retained.Image.RawImage) == currentHash && retained.Image.RawImage.SequenceEqual(freshSaved.Image.RawImage));
                var partial = PresetBody.Decode(d, retained.Image);
                EditorPreservation.VerifySceneMidiInsertion(d, baseline.Image, retained.Image, row, column,
                    BinaryPrimitives.ReadUInt16LittleEndian(retained.Image.Body.AsSpan(0, 2)) + 1);
                Check("partial-pilot-unrelated-body-preserved", true);
                Check("partial-pilot-sole-owned-block", partial.Grid.Where(c => c.EffectId != 0).SequenceEqual(new[] { new GridCell(row, column, 190, original.Grid.Single(c => c.Row == row && c.Column == column).RouteFlag) { Name = "Scene MIDI" } }));
                Check("partial-pilot-live-table-matches", (await session.SceneMidiAsync(context, timeout, token)).Fields.SequenceEqual(SceneMidiTable.SavedFields(d, partial.BlockRecords)));
                session.AuthorizeEmptyPresetPilot(scope); stage = "restore-retained-partial-pilot";
                await session.SendEditorAsync(EditorCommand.SelectCell(d, slot, row, column), timeout, token);
                await session.SendEditorAsync(EditorCommand.ClearCell(d, slot, row, column), timeout, token);
                Check("partial-recovery-live-empty", (await session.BlockStatusAsync(context, timeout, token)).All(e => e.EffectId is 200 or 201));
                await session.SendEditorAsync(EditorCommand.SelectScene(d, slot, defaultScene), timeout, token);
                Check("partial-recovery-selection", (await session.CurrentAsync(context, timeout, token)) is var state && state.Slot == slot && state.Scene == defaultScene);
                Check("partial-recovery-save-base", (await session.SavedAsync(context, slot, timeout, token)).Image.RawImage.SequenceEqual(retained.Image.RawImage));
                await session.SendEditorAsync(EditorCommand.SavePreset(d, slot), timeout, token);
                var final = await session.SavedAsync(context, slot, timeout, token);
                SnapshotExport.Write(Path.Combine(folder, "restored-original"), context, [final]);
                lastSavedHash = PresetBody.Sha256(final.Image.RawImage);
                Check("partial-recovery-saved-body-identical", final.Image.Body.SequenceEqual(baseline.Image.Body));
                Check("partial-recovery-saved-raw-identical", final.Image.RawImage.SequenceEqual(baseline.Image.RawImage));
                await session.SendEditorAsync(EditorCommand.ReloadPreset(d, slot), timeout, token);
                after = await session.CurrentAsync(context, timeout, token);
                Check("partial-recovery-final-live-empty", after.Slot == slot && after.Scene == defaultScene &&
                    (await session.BlockStatusAsync(context, timeout, token)).All(e => e.EffectId is 200 or 201));
                Check("partial-recovery-final-saved-identical", (await session.SavedAsync(context, slot, timeout, token)).Image.RawImage.SequenceEqual(baseline.Image.RawImage));
                restored = true; complete = true; stage = "complete";
            }
            else if (recoverOnly)
            {
                // A fresh saved read proves there is no persistent change to undo. Reload once, then verify.
                session.AuthorizeEmptyPresetPilot(scope);
                stage = "reload-unchanged-saved-baseline";
                await session.SendEditorAsync(EditorCommand.ReloadPreset(d, slot), timeout, token);
                after = await session.CurrentAsync(context, timeout, token);
                Check("recovery-target-and-scene", after.Slot == slot && after.Scene == defaultScene);
                Check("recovery-live-empty", (await session.BlockStatusAsync(context, timeout, token)).All(e => e.EffectId is 200 or 201));
                Check("recovery-saved-identical", (await session.SavedAsync(context, slot, timeout, token)).Image.RawImage.SequenceEqual(baseline.Image.RawImage));
                restored = true; complete = true; stage = "complete";
            }
            else
            {
            var baselineStatus = await session.BlockStatusAsync(context, timeout, token);
            // fn13 includes always-present foot-controller/preset-FC entries (200/201), which are not grid cells.
            Check("live-grid-empty", baselineStatus.All(e => e.EffectId is 200 or 201));
            int[] baselineIds = baselineStatus.Select(e => e.EffectId).Order().ToArray();
            Check("live-scene-names-match-backup", (await TaskNames()).SequenceEqual(baseline.Image.SceneNames));
            session.AuthorizeEmptyPresetPilot(scope);
            async Task<string[]> TaskNames()
            {
                List<string> names = []; for (int scene = 1; scene <= 8; scene++) names.Add(await session.SceneNameAsync(context, scene, timeout, token));
                return names.ToArray();
            }
            async Task Send(EditorCommand command)
            {
                Check($"target-before-{command.Operation}", (await session.CurrentAsync(context, timeout, token)).Slot == slot);
                await session.SendEditorAsync(command, timeout, token);
            }
            async Task Insert()
            {
                await Send(EditorCommand.SelectCell(d, slot, row, column));
                await Send(EditorCommand.InsertBlock(d, slot, row, column, 190));
                Check("live-inserted-instance", (await session.BlockStatusAsync(context, timeout, token)).Select(e => e.EffectId).Order().SequenceEqual(baselineIds.Append(190).Order()));
            }
            async Task Clear()
            {
                await Send(EditorCommand.SelectCell(d, slot, row, column));
                await Send(EditorCommand.ClearCell(d, slot, row, column));
                Check("live-block-removed", (await session.BlockStatusAsync(context, timeout, token)).Select(e => e.EffectId).Order().SequenceEqual(baselineIds));
            }
            async Task<SavedCapture> Capture(string name)
            {
                var c = await session.SavedAsync(context, slot, timeout, token);
                SnapshotExport.Write(Path.Combine(folder, name), context, [c]); return c;
            }
            async Task<SavedCapture> Save(string name, int scene)
            {
                Check("save-base-not-changed", PresetBody.Sha256((await session.SavedAsync(context, slot, timeout, token)).Image.RawImage) == lastSavedHash);
                await Send(EditorCommand.SelectScene(d, slot, scene));
                Check("scene-before-save", (await session.CurrentAsync(context, timeout, token)).Scene == scene);
                await Send(EditorCommand.SavePreset(d, slot));
                var c = await Capture(name); lastSavedHash = PresetBody.Sha256(c.Image.RawImage); return c;
            }
            async Task Reload()
            {
                await Send(EditorCommand.ReloadPreset(d, slot));
                Check("reload-authorized-target", (await session.CurrentAsync(context, timeout, token)).Slot == slot);
            }

            stage = "transient-insertion-and-removal";
            await Insert();
            var initialTable = await session.SceneMidiAsync(context, timeout, token);
            File.WriteAllText(Path.Combine(folder, "new-block-defaults.private.json"), SnapshotExport.Json(initialTable));
            Check("new-block-actual-defaults-decodable", SceneMidiTable.Decode(d, initialTable.Fields).All(s => s.Length == 8));
            await Clear();
            Check("transient-test-saved-state-unchanged", (await Capture("after-transient")).Image.RawImage.SequenceEqual(baseline.Image.RawImage));
            await Reload();
            Check("transient-reload-empty", (await session.BlockStatusAsync(context, timeout, token)).Select(e => e.EffectId).Order().SequenceEqual(baselineIds));

            stage = "persistent-pc-cc-pilot";
            await Insert();
            var defaults = await session.SceneMidiAsync(context, timeout, token);
            Check("repeat-new-block-defaults", defaults.Fields.SequenceEqual(initialTable.Fields));
            int[] desired = defaults.Fields.ToArray(); var layout = d.SceneMidi!;
            for (int scene = 0; scene < 8; scene++)
            {
                int pc = scene * 8, cc = pc + 1;
                desired[layout.TypeBank + pc] = 2; desired[layout.ChannelBank + pc] = scene % 2 == 0 ? 0 : 15;
                desired[layout.ValueBank + pc] = scene % 2 == 0 ? 1 : 127;
                desired[layout.TypeBank + cc] = 1; desired[layout.ChannelBank + cc] = 15;
                desired[layout.ControllerBank + cc] = 119; desired[layout.ValueBank + cc] = scene % 2 == 0 ? 0 : 127;
            }
            // Repeated pilots can inherit the previously used instance's settings. Always exercise real writes.
            desired[layout.ValueBank] = defaults.Fields[layout.ValueBank] == 1 ? 2 : 1;
            desired[layout.ControllerBank + 1] = defaults.Fields[layout.ControllerBank + 1] == 119 ? 118 : 119;
            // Inactive/opaque fields and other message rows retain their actual device defaults.
            for (int p = 0; p < desired.Length; p++) if (desired[p] != defaults.Fields[p])
                await Send(EditorCommand.SetSceneMidiField(d, slot, p, desired[p]));
            var configured = await session.SceneMidiAsync(context, timeout, token);
            Check("complete-live-table-pc-cc", configured.Fields.SequenceEqual(desired));
            Check("live-opaque-extra-preserved", configured.ExtraWord == defaults.ExtraWord);

            int alternateScene = defaultScene == 7 ? 8 : 7;
            var varied = await Save("saved-alternate-default", alternateScene);
            Check("saved-default-follows-selected-scene", BinaryPrimitives.ReadUInt16LittleEndian(varied.Image.Body.AsSpan(0, 2)) == alternateScene - 1);
            CheckSaved(varied, desired, alternateScene);
            var saved = await Save("saved-preserved-default", defaultScene);
            Check("original-default-scene-preserved", BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body.AsSpan(0, 2)) == defaultScene - 1);
            CheckSaved(saved, desired, defaultScene);
            await Reload();
            Check("saved-default-after-reload", (await session.CurrentAsync(context, timeout, token)).Scene == defaultScene);
            Check("complete-pc-cc-table-after-reload", (await session.SceneMidiAsync(context, timeout, token)).Fields.SequenceEqual(desired));
            Check("saved-state-stable-after-reload", (await Capture("after-pilot-reload")).Image.RawImage.SequenceEqual(saved.Image.RawImage));

            void CheckSaved(SavedCapture c, int[] fields, int savedScene)
            {
                var decoded = PresetBody.Decode(d, c.Image);
                Check("chosen-grid-cell", decoded.Grid.Single(g => g.EffectId == 190) is var g && g.Row == row && g.Column == column);
                Check("complete-saved-table", SceneMidiTable.SavedFields(d, decoded.BlockRecords).SequenceEqual(fields));
                Check("names-preserved", c.Image.Name == baseline.Image.Name && c.Image.SceneNames.SequenceEqual(baseline.Image.SceneNames));
                EditorPreservation.VerifySceneMidiInsertion(d, baseline.Image, c.Image, row, column, savedScene);
                Check("unrelated-body-bytes-preserved", true);
            }

            stage = "restore-original-empty-preset";
            await Clear();
            var final = await Save("restored-original", defaultScene);
            await Reload();
            after = await session.CurrentAsync(context, timeout, token);
            Check("final-live-empty", (await session.BlockStatusAsync(context, timeout, token)).Select(e => e.EffectId).Order().SequenceEqual(baselineIds));
            Check("original-saved-body-restored", final.Image.Body.SequenceEqual(baseline.Image.Body));
            Check("original-saved-raw-image-restored", final.Image.RawImage.SequenceEqual(baseline.Image.RawImage));
            Check("final-saved-read-identical", (await Capture("final-readback")).Image.RawImage.SequenceEqual(baseline.Image.RawImage));
            Check("original-selection-restored", after == before);
            restored = true; complete = true; stage = "complete";
            }
        }
        catch (Exception error) { failure = $"{error.GetType().Name}: {error.Message}"; }
        var report = new { protocol_release = ProtocolRelease.Version, complete, restored, stage, failure, device = context,
            slot, row, column, state_before = before, state_after = after, expected_base_sha256 = expectedHash,
            last_observed_saved_sha256 = lastSavedHash, results, transmissions = trace.Sends, receives = trace.Receives,
            limitations = new[] { "Empty-preset operation-based recovery only; full-dump restore unqualified", "No production writer or batch execution", "Other models/firmware require physical qualification", "External MIDI receiver behavior and independent editor display not observed" } };
        File.WriteAllText(Path.Combine(folder, "report.private.json"), SnapshotExport.Json(report));
        Console.WriteLine(SnapshotExport.Json(new { complete, restored, stage, failure, model = d.Model, firmware = context?.Firmware, slot }));
        return complete ? 0 : 1;
    }
}
