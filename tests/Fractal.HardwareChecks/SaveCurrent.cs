using Fractal.Core;

/// <summary>Persist an already-loaded preset, verifying against an integrity-checked expected image.
/// This is not a file upload: the expected file is comparison evidence and is never transmitted.</summary>
static class SaveCurrent
{
    public static async Task<int> Run(int input, int output, DeviceDefinition d, string folder, Dictionary<string, string> options)
    {
        int slot = int.Parse(options["--preset"]), scene = int.Parse(options["--scene"]);
        d.ValidateSlot(slot);
        if (scene is < 1 or > 8) throw new ArgumentException("Scene must be 1–8.");
        byte[] Load(string path, string hash)
        {
            if (new FileInfo(path).Length > 60000) throw new InvalidDataException("Single-preset file exceeds size bound.");
            byte[] bytes = File.ReadAllBytes(path);
            if (!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(bytes), hash))
                throw new InvalidDataException("Retained file hash does not match the authorized input.");
            return bytes;
        }
        byte[] expectedWire = Load(options["--backup"], options["--expected-hash"]);
        PresetFileInspection expected = PresetFile.Inspect(d, expectedWire);
        byte[] baseWire = Load(options["--current-backup"], options["--current-hash"]);
        SavedCapture baseline = SnapshotExport.DecodeFile(d, baseWire);
        if (baseline.Image.Slot != slot) throw new InvalidDataException("Retained base is for another slot.");
        // Caller supplies exact, model-qualified instance IDs, rather than shape-based block guesses.
        var channels = options["--expected-channels"].Split(',').Select(item =>
        {
            string[] parts = item.Split(':');
            if (parts.Length != 2) throw new ArgumentException("Expected channels use EFFECT_ID:CHANNEL pairs.");
            int id = int.Parse(parts[0]), channel = int.Parse(parts[1]);
            if (id is < 1 or > 1000 || channel is < 1 or > 4) throw new ArgumentException("Invalid expected block/channel.");
            return (Id: id, Channel: channel);
        }).ToArray();
        if (channels.Length == 0 || channels.Select(c => c.Id).Distinct().Count() != channels.Length)
            throw new ArgumentException("Require distinct expected block channels.");
        var results = new List<object>();
        DeviceContext? context = null;
        CurrentState? before = null, after = null;
        string? failure = null;
        bool saveAttempted = false, rawEqual = false, bodyEqual = false, repeatedEqual = false;
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        CancellationToken token = cancellation.Token;
        TimeSpan timeout = TimeSpan.FromSeconds(10);
        using var trace = new Trace(Path.Combine(folder, "trace.jsonl"));
        void Check(string name, bool passed)
        { results.Add(new { name, passed }); if (!passed) throw new InvalidDataException($"Save-current check failed: {name}"); }
        try
        {
            var scope = new PresetSaveScope(d, slot);
            await using var midi = new TracedMidi(await TransportConnection.OpenAsync(input, output, scope), trace);
            await using var session = new ReadSession(midi);
            context = await session.IdentifyAsync(d.Model, d.PresetCapacity, timeout, token);
            Check("model-and-recorded-firmware", context.Definition == d && context.Firmware == options["--firmware"]);
            before = await session.CurrentAsync(context, timeout, token);
            Check("selected-target-scene-and-name", before.Slot == slot && before.Scene == scene && before.Name == expected.Name);
            SavedCapture current = await session.SavedAsync(context, slot, timeout, token);
            Check("saved-base-exact", current.Sysex.SequenceEqual(baseline.Sysex) &&
                current.Image.RawImage.SequenceEqual(baseline.Image.RawImage) && current.Image.Body.SequenceEqual(baseline.Image.Body));
            SnapshotExport.Write(Path.Combine(folder, "before-save"), context, [current], before, before);
            for (int i = 1; i <= 8; i++)
                Check($"live-scene-name-{i}", await session.SceneNameAsync(context, i, timeout, token) == expected.SceneNames[i - 1]);
            var status = await session.BlockStatusAsync(context, timeout, token);
            File.WriteAllText(Path.Combine(folder, "before-status.private.json"), SnapshotExport.Json(status));
            Check("expected-live-channels", channels.All(c => status.Any(b => b.EffectId == c.Id && b.Channel == c.Channel)));
            Check("selection-stable-before-save", await session.CurrentAsync(context, timeout, token) == before);
            // Status/names are partial live evidence. Only complete saved-image readback can establish equality.
            session.AuthorizePresetSave(scope);
            saveAttempted = true;
            await session.SendEditorAsync(EditorCommand.SavePreset(d, slot), timeout, token);
            SavedCapture first = await session.SavedAsync(context, slot, timeout, token);
            after = await session.CurrentAsync(context, timeout, token);
            SnapshotExport.Write(Path.Combine(folder, "after-save"), context, [first], before, after);
            rawEqual = first.Image.RawImage.SequenceEqual(expected.RawImage);
            bodyEqual = first.Image.Body.SequenceEqual(expected.Body);
            Check("complete-raw-image-matches-backup", rawEqual);
            Check("complete-decoded-body-matches-backup", bodyEqual);
            Check("selection-preserved-after-save", after == before);
            SavedCapture second = await session.SavedAsync(context, slot, timeout, token);
            SnapshotExport.Write(Path.Combine(folder, "repeat-after-save"), context, [second]);
            repeatedEqual = second.Sysex.SequenceEqual(first.Sysex) && second.Image.RawImage.SequenceEqual(expected.RawImage) &&
                second.Image.Body.SequenceEqual(expected.Body);
            Check("repeat-complete-content", repeatedEqual);
            after = await session.CurrentAsync(context, timeout, token);
            Check("final-selection-preserved", after == before);
        }
        catch (Exception error) { failure = $"{error.GetType().Name}: {error.Message}"; }
        var report = new { protocol_release = ProtocolRelease.Version, completed_utc = DateTimeOffset.UtcNow,
            operation = "save-already-loaded-preset", context, slot, scene, state_before = before, state_after = after,
            complete = failure == null, failure, save_attempted = saveAttempted,
            raw_equal_original = rawEqual, body_equal_original = bodyEqual, repeated_saved_equal = repeatedEqual,
            expected_file_sha256 = PresetBody.Sha256(expectedWire), base_file_sha256 = PresetBody.Sha256(baseWire),
            expected_raw_sha256 = PresetBody.Sha256(expected.RawImage), expected_body_sha256 = PresetBody.Sha256(expected.Body),
            results, transmissions = trace.Sends, received_frames = trace.Receives,
            limitations = new[] { "Expected backup file was not transmitted; full-file upload remains unqualified",
                "Live status is partial evidence; complete saved readback establishes the result",
                "No reload, other-slot comparison or automatic recovery; only connected model/firmware tested" } };
        File.WriteAllText(Path.Combine(folder, "report.private.json"), SnapshotExport.Json(report));
        Console.WriteLine(SnapshotExport.Json(new { complete = failure == null, failure, save_attempted = saveAttempted,
            raw_equal_original = rawEqual, body_equal_original = bodyEqual, repeated_saved_equal = repeatedEqual,
            state_preserved = before != null && before == after, model = context?.Definition.Model,
            firmware = context?.Firmware, slot, transmissions = trace.Sends }));
        return failure == null ? 0 : 1;
    }
}
