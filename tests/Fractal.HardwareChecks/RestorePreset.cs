using Fractal.Core;

// Qualification harness. All operating choices are caller inputs; captures and traces are private.
static class RestorePreset
{
    public static async Task<int> Run(int input, int output, DeviceDefinition d, string folder, Dictionary<string, string> options)
    {
        int slot = int.Parse(options["--preset"]), scene = int.Parse(options["--scene"]);
        byte[] Load(string file, string hash)
        {
            if (new FileInfo(file).Length > 60000) throw new InvalidDataException("Single-preset file exceeds size bound.");
            byte[] bytes = File.ReadAllBytes(file);
            if (!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(bytes), hash)) throw new InvalidDataException("Retained file hash mismatch.");
            return bytes;
        }
        byte[] source = Load(options["--backup"], options["--expected-hash"]);
        var plan = PresetUploadPlan.Create(d, slot, scene, source, options["--expected-hash"]);
        var retained = SnapshotExport.DecodeFile(d, Load(options["--current-backup"], options["--current-hash"]));
        if (retained.Image.Slot != slot) throw new InvalidDataException("Retained saved base is for another slot.");
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        CancellationToken token = cancellation.Token;
        TimeSpan timeout = TimeSpan.FromSeconds(10);
        using var trace = new Trace(Path.Combine(folder, "trace.jsonl"));
        var results = new List<object>();
        string stage = "preflight", failure = "";
        DeviceContext? context = null;
        CurrentState? before = null, after = null;
        bool uploaded = false, bufferVerified = false, savedVerified = false, reloaded = false, otherSlotsPreserved = false;
        SavedCapture[]? inventoryBefore = null, inventoryAfter = null;
        void Check(string name, bool ok)
        { results.Add(new { name, passed = ok }); if (!ok) throw new InvalidDataException($"Restore check failed: {name}"); }
        try
        {
            var scope = new PresetRestoreScope(plan);
            await using var midi = new TracedMidi(await TransportConnection.OpenAsync(input, output, scope), trace);
            await using var session = new ReadSession(midi);
            context = await session.IdentifyAsync(d.Model, d.PresetCapacity, timeout, token);
            Check("exact-model-and-firmware", context.Definition == d && context.Firmware == options["--firmware"]);
            before = await session.CurrentAsync(context, timeout, token);
            Check("authorized-selected-target-and-scene", before.Slot == slot && before.Scene == scene);
            async Task<SavedCapture[]> Inventory(string label)
            {
                CurrentState start = await session.CurrentAsync(context, timeout, token);
                var captures = new List<SavedCapture>();
                for (int address = 0; address < d.PresetCapacity; address++)
                {
                    captures.Add(await session.SavedAsync(context, address, timeout, token));
                    if ((address + 1) % 64 == 0)
                    {
                        Console.WriteLine($"{label}: read {address + 1}/{d.PresetCapacity}");
                        Check($"{label}-selection-{address + 1}", start == await session.CurrentAsync(context, timeout, token));
                    }
                }
                var end = await session.CurrentAsync(context, timeout, token);
                Check($"{label}-selection-preserved", start == end);
                SnapshotExport.Write(Path.Combine(folder, label), context, captures.ToArray(), start, end);
                return captures.ToArray();
            }
            stage = "inventory-before"; inventoryBefore = await Inventory(stage);
            session.AuthorizePresetRestore(scope);
            stage = "fresh-base-and-live-buffer";
            var fresh = await session.PreparePresetRestoreAsync(context, options["--current-hash"], timeout, token);
            Check("retained-current-image-exact", fresh.Sysex.SequenceEqual(retained.Sysex));
            SnapshotExport.Write(Path.Combine(folder, "fresh-base"), context, [fresh]);
            stage = "upload";
            await session.UploadPresetAsync(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), token); uploaded = true;
            stage = "verify-loaded";
            var loaded = await session.VerifyUploadedPresetAsync(context, timeout, token); bufferVerified = true;
            File.WriteAllBytes(Path.Combine(folder, "uploaded-buffer.private.syx"), loaded.Sysex);
            // Prove temporary-buffer upload did not persist the target before issuing Save.
            var notSaved = await session.SavedAsync(context, slot, timeout, token);
            Check("upload-left-target-saved-base-unchanged", notSaved.Sysex.SequenceEqual(fresh.Sysex));
            stage = "save";
            await session.SendEditorAsync(EditorCommand.SavePreset(d, slot), timeout, token);
            stage = "verify-saved";
            var first = await session.VerifyRestoredSavedAsync(context, timeout, token); savedVerified = true;
            SnapshotExport.Write(Path.Combine(folder, "saved-result"), context, [first]);
            var repeat = await session.VerifyRestoredSavedAsync(context, timeout, token);
            Check("repeated-saved-content-exact", repeat.Sysex.SequenceEqual(first.Sysex));
            stage = "reload";
            await session.SendEditorAsync(EditorCommand.ReloadPreset(d, slot), timeout, token); reloaded = true;
            stage = "verify-reloaded";
            var reloadedBuffer = await session.VerifyUploadedPresetAsync(context, timeout, token);
            File.WriteAllBytes(Path.Combine(folder, "reloaded-buffer.private.syx"), reloadedBuffer.Sysex);
            var final = await session.VerifyRestoredSavedAsync(context, timeout, token);
            Check("reloaded-saved-content-exact", final.Sysex.SequenceEqual(first.Sysex));
            SnapshotExport.Write(Path.Combine(folder, "after-reload"), context, [final]);
            stage = "inventory-after"; inventoryAfter = await Inventory(stage);
            int[] differences = inventoryBefore.Where(c => c.Image.Slot != slot).Where(c =>
            {
                var other = inventoryAfter.Single(x => x.Image.Slot == c.Image.Slot);
                return !other.Sysex.SequenceEqual(c.Sysex) || !other.Image.RawImage.SequenceEqual(c.Image.RawImage) || !other.Image.Body.SequenceEqual(c.Image.Body);
            }).Select(c => c.Image.Slot).ToArray();
            Check("every-other-saved-slot-unchanged", differences.Length == 0); otherSlotsPreserved = true;
            after = await session.CurrentAsync(context, timeout, token);
            Check("final-target-and-scene", after.Slot == slot && after.Scene == scene);
            stage = "complete";
        }
        catch (Exception error) { failure = $"{error.GetType().Name}: {error.Message}"; }
        var report = new { protocol_release = ProtocolRelease.Version, completed_utc = DateTimeOffset.UtcNow,
            operation = "single-preset-upload-save-reload-qualification", context, slot, scene,
            state_before = before, state_after = after, complete = failure.Length == 0, stage, failure,
            source_sha256 = plan.SourceSha256, upload_sha256 = plan.UploadSha256,
            expected_raw_sha256 = plan.RawSha256, expected_body_sha256 = plan.BodySha256,
            uploaded, buffer_verified = bufferVerified, saved_verified = savedVerified, reloaded,
            other_saved_slots_verified = otherSlotsPreserved ? d.PresetCapacity - 1 : 0,
            inventory_before_count = inventoryBefore?.Length ?? 0, inventory_after_count = inventoryAfter?.Length ?? 0,
            checks = results, transmissions = trace.Sends, received_frames = trace.Receives,
            profile_evidence = "forgefx-gen3-edit-buffer; external FM3 live-validation claim; this connected model/firmware trial recorded separately",
            limits = new[] { "One selected target and same-model source only", "No bank/system/cab/firmware restore or format conversion",
                "No hardware interrupted-transfer or power-loss recovery trial", "Other model/firmware physical qualification is separate" } };
        File.WriteAllText(Path.Combine(folder, "report.private.json"), SnapshotExport.Json(report));
        Console.WriteLine(SnapshotExport.Json(new { complete = failure.Length == 0, stage, failure,
            model = context?.Definition.Model, firmware = context?.Firmware, uploaded, buffer_verified = bufferVerified,
            saved_verified = savedVerified, reloaded, other_saved_slots_verified = otherSlotsPreserved ? d.PresetCapacity - 1 : 0 }));
        return failure.Length == 0 ? 0 : 1;
    }
}
