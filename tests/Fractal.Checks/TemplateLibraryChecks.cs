using System.Buffers.Binary;
using System.Text.Json;
using Fractal.Core;

static class TemplateLibraryChecks
{
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        var fixture = AudioRoutingChecks.Build(d); byte[] body = fixture.Body;
        Put(body, 0, 2); int shape = fixture.Offsets[66], columns = d.Model == "FM9" ? 73 : 71, width = columns * 2;
        for (int row = 0; row < 4; row++) for (int word = 0; word < columns; word++) Put(body, shape + 34 + row * width + word * 2, 1000 * (row + 1) + word);
        for (int s = 1; s <= 8; s++) { Put(body, shape + (s - 2) * 2, (s - 1) % 4); Put(body, shape + (s + 6) * 2, s % 2); }
        var baseline = Fixtures.FromBody(d, 0, body);
        PresetEditRequest Request(params PresetEditOperation[] operations) => new(1, d.Model, d.PresetCapacity, "99.07", 0, "save", PresetBody.Sha256(baseline.Sysex), operations);
        var swap = new PresetEditOperation("swap-channels", EffectId: 66, Channel: "A", OtherChannel: "C", Policy: "preserve-scene-references");
        byte[] Expected(bool follow)
        {
            byte[] expected = body.ToArray();
            // Literal row intervals, independent of the planner's projection/operation code.
            body.AsSpan(shape + 34 + 2 * width, width).CopyTo(expected.AsSpan(shape + 34, width));
            body.AsSpan(shape + 34, width).CopyTo(expected.AsSpan(shape + 34 + 2 * width, width));
            if (follow) foreach (int s in new[] { 1, 3, 5, 7 }) Put(expected, shape + (s - 2) * 2, s is 1 or 5 ? 2 : 0);
            return expected;
        }
        var preserve = PresetEditPlan.Create(Request(swap), baseline.Sysex);
        var follow = PresetEditPlan.Create(Request(swap with { Policy = "follow-channel-settings" }), baseline.Sysex);
        Need(preserve.Executable && follow.Executable, "Both explicit swap policies executable");
        Need(SnapshotExport.DecodeFile(d, preserve.Expected).Image.Body.SequenceEqual(Expected(false)), "Preserve-policy complete body including unchanged shared data");
        Need(SnapshotExport.DecodeFile(d, follow.Expected).Image.Body.SequenceEqual(Expected(true)), "Follow-policy changes only selected rows and their scene channel references");
        Need(!PresetEditPlan.Create(Request(swap with { OtherChannel = "A" }), baseline.Sysex).Executable, "Distinct channels required");
        Need(!PresetEditPlan.Create(Request(swap with { OtherChannel = "E" }), baseline.Sysex).Executable, "Channel bounds");
        Need(!PresetEditPlan.Create(Request(swap with { Policy = null }), baseline.Sysex).Executable, "Explicit reference policy required");
        Need(!PresetEditPlan.Create(Request(swap with { Name = "unexpected" }), baseline.Sysex).Executable, "Swap-specific field refusal");
        Need(!PresetEditPlan.Create(Request(swap, new("set-scene-block-state", EffectId: 66, Scene: 2, Channel: "B")), baseline.Sysex).Executable, "Overlapping instance edits refused");
        Need(!PresetEditPlan.Create(Request(swap, new("set-modifier-source", ModifierSlot: 1, SourceId: 1, ExpectedBinding: [0, 58, 1])), baseline.Sysex).Executable, "Shared modifier composition refused");
        Need(PresetEditPlan.Create(Request(swap, new("rename-scene", Scene: 2, Name: "Synthetic")), baseline.Sysex).Executable, "Unrelated operations compose");
        var unknownBody = body.ToArray(); Put(unknownBody, shape - 2, 4);
        var unknown = Fixtures.FromBody(d, 0, unknownBody);
        Need(!PresetEditPlan.Create(Request(swap) with { BaseSha256 = PresetBody.Sha256(unknown.Sysex) }, unknown.Sysex).Executable, "Unknown scene reference is refused");
        var identicalBody = body.ToArray(); body.AsSpan(shape + 34, width).CopyTo(identicalBody.AsSpan(shape + 34 + 2 * width, width));
        var identical = Fixtures.FromBody(d, 0, identicalBody);
        var noop = PresetEditPlan.Create(Request(swap with { Policy = "follow-channel-settings" }) with { BaseSha256 = PresetBody.Sha256(identical.Sysex) }, identical.Sysex);
        Need(noop.Executable && !noop.Changed && noop.Expected.SequenceEqual(identical.Sysex), "Identical-row no-op retains scene references and original wire bytes");
        var reverse = PresetEditPlan.Create(Request(swap with { Policy = "follow-channel-settings" }) with { BaseSha256 = follow.ExpectedSha256 }, follow.Expected);
        Need(reverse.Executable && SnapshotExport.DecodeFile(d, reverse.Expected).Image.Body.SequenceEqual(body), "Fresh intentional second swap reverses saved body");

        string root = Path.Combine(Path.GetTempPath(), "fractal-templates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string library = Path.Combine(root, "library");
            var metadata = new TemplateImportRequest(1, d.Model, d.PresetCapacity, "99.07", 66, "channel", "Synthetic channel", ["example", "delay"], "Synthetic independently filled channel rows", PresetBody.Sha256(baseline.Sysex), "C");
            var result = JsonSerializer.SerializeToElement(TemplateLibrary.Import(library, metadata, baseline.Sysex), SnapshotExport.JsonOptions);
            string id = result.GetProperty("entry_id").GetString()!, hash = result.GetProperty("entry_sha256").GetString()!;
            string path = TemplateLibrary.EntryPath(library, id); byte[] originalEntry = File.ReadAllBytes(path);
            var entry = TemplateLibrary.ReadEntry(path, hash);
            Need(entry.Columns == columns && entry.SettingsSha256 == PresetBody.Sha256(body.AsSpan(shape + 34 + 2 * width, width).ToArray()), "Library derives exact row and shape from donor");
            var listed = JsonSerializer.SerializeToElement(TemplateLibrary.List(library, "CHANNEL", "DELAY"), SnapshotExport.JsonOptions);
            Need(listed.GetProperty("total").GetInt32() == 1 && !listed.GetRawText().Contains(entry.Capture), "Search returns private metadata without capture bytes");
            var apply = new TemplateApplyRequest(1, d.Model, d.PresetCapacity, "99.07", 0, "save", PresetBody.Sha256(baseline.Sysex), 66, "copy-channel", "A");
            var copied = TemplateLibrary.Plan(path, hash, apply, baseline.Sysex);
            byte[] copiedExpected = body.ToArray(); body.AsSpan(shape + 34 + 2 * width, width).CopyTo(copiedExpected.AsSpan(shape + 34, width));
            Need(copied.Executable && SnapshotExport.DecodeFile(d, copied.Expected).Image.Body.SequenceEqual(copiedExpected), "Library lowers to ordinary row copy with full-body preservation");
            Need(!TemplateLibrary.Plan(path, hash, apply with { BaseSha256 = copied.ExpectedSha256 }, copied.Expected).Changed, "Repeat template application is a no-op");
            Reject<ArgumentException>(() => TemplateLibrary.Plan(path, hash, apply with { Firmware = "99.08" }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.Plan(path, hash, apply with { Model = d.Model == "FM9" ? "FM3" : "FM9" }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.Plan(path, hash, apply with { Capacity = d.PresetCapacity == 512 ? 1024 : 512 }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.Plan(path, hash, apply with { EffectId = 70 }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.Plan(path, hash, apply with { Kind = "copy-block-settings", Channel = null }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.Create(metadata with { Tags = ["same", "SAME"] }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.Create(metadata with { Kind = "block" }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.Create(metadata with { CaptureSha256 = new string('0', 64) }, baseline.Sysex));
            Reject<ArgumentException>(() => TemplateLibrary.EntryPath(library, "../escape"));
            Reject<InvalidDataException>(() => TemplateLibrary.Parse<TemplateImportRequest>(System.Text.Encoding.UTF8.GetBytes("{\"schema_version\":1,\"schema_version\":1}")));
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(entry with { Columns = entry.Columns + 1 }, PresetEditPlan.RequestJson));
            Reject<InvalidDataException>(() => TemplateLibrary.ReadEntry(path, PresetBody.Sha256(File.ReadAllBytes(path))));
            File.WriteAllBytes(path, originalEntry); File.AppendAllText(path, " ");
            Reject<InvalidDataException>(() => TemplateLibrary.ReadEntry(path, hash)); File.WriteAllBytes(path, originalEntry);
            // Freeze application bytes in the normal bundle; later library changes cannot change the reviewed edit.
            string bundle = Path.Combine(root, "bundle"); copied.WriteBundle(bundle); File.AppendAllText(path, " ");
            Need(PresetEditPlan.OpenBundle(bundle, copied.PlanSha256).Expected.SequenceEqual(copied.Expected), "Frozen bundle is independent of mutable library files"); File.WriteAllBytes(path, originalEntry);
            var oldReport = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(bundle, "plan.json")))!;
            oldReport["protocol_release"] = "0.34.0";
            File.WriteAllText(Path.Combine(bundle, "plan.json"), oldReport.ToJsonString());
            string oldHash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(bundle, "plan.json")));
            Need(PresetEditPlan.OpenBundle(bundle, oldHash, allowPriorReceiptRelease: true).Expected.SequenceEqual(copied.Expected),
                "Compatible synthetic prior-release receipt revalidates all content while allowing only release label difference");
            Reject<InvalidDataException>(() => PresetEditPlan.OpenBundle(bundle, oldHash));
            oldReport["expected_sha256"] = new string('0', 64); File.WriteAllText(Path.Combine(bundle, "plan.json"), oldReport.ToJsonString());
            Reject<InvalidDataException>(() => PresetEditPlan.OpenBundle(bundle, PresetBody.Sha256(File.ReadAllBytes(Path.Combine(bundle, "plan.json"))), allowPriorReceiptRelease: true));
            TemplateLibrary.Import(library, metadata with { Name = "Second synthetic" }, baseline.Sysex);
            var page = JsonSerializer.SerializeToElement(TemplateLibrary.List(library, limit: 1), SnapshotExport.JsonOptions);
            Need(page.GetProperty("total").GetInt32() == 2 && page.GetProperty("next_offset").GetInt32() == 1, "Bounded library paging");
            var donorBody = body.ToArray(); Put(donorBody, shape - 2, 1); Put(donorBody, shape + 34 + width, 4444);
            var donor = Fixtures.FromBody(d, 1, donorBody);
            var blockResult = JsonSerializer.SerializeToElement(TemplateLibrary.Import(library, metadata with { Kind = "block", Channel = null, CaptureSha256 = PresetBody.Sha256(donor.Sysex) }, donor.Sysex), SnapshotExport.JsonOptions);
            var blockPlan = TemplateLibrary.Plan(TemplateLibrary.EntryPath(library, blockResult.GetProperty("entry_id").GetString()!), blockResult.GetProperty("entry_sha256").GetString()!, apply with { Kind = "copy-block-settings", Channel = null }, baseline.Sysex);
            Need(SnapshotExport.DecodeFile(d, blockPlan.Expected).Image.Body.SequenceEqual(donorBody), "Whole-block template includes scene assignments and settings only");
            string blockPath = TemplateLibrary.EntryPath(library, blockResult.GetProperty("entry_id").GetString()!);
            string blockHash = blockResult.GetProperty("entry_sha256").GetString()!;
            var removed = PresetEditPlan.Create(Request(new PresetEditOperation("remove-block", EffectId: 66, Policy: "replace-with-shunt", Value: 1026)), baseline.Sysex);
            Need(removed.Executable, "Synthetic target prepared without donor instance");
            var inserted = TemplateLibrary.Plan(blockPath, blockHash, apply with { Kind = "insert-block", Channel = null,
                Row = 1, Column = 3, Policy = "replace-shunt", BaseSha256 = removed.ExpectedSha256 }, removed.Expected);
            Need(inserted.Executable && PresetEditProjection.Decode(d, "99.07", inserted.Expected).Blocks.Single(b => b.EffectId == 66).Record?.Columns == columns,
                "Block library entry uses ordinary connected insertion");
            var invalidPlacement = TemplateLibrary.Plan(blockPath, blockHash, apply with { Kind = "insert-block", Channel = null,
                Row = d.GridRows + 1, Column = 3, BaseSha256 = removed.ExpectedSha256 }, removed.Expected);
            Need(!invalidPlacement.Executable, "Model grid bounds remain planner conflicts");
            var shapeEntry = entry with { Rows = 1 };
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(shapeEntry, PresetEditPlan.RequestJson));
            Reject<InvalidDataException>(() => TemplateLibrary.ReadEntry(path, PresetBody.Sha256(File.ReadAllBytes(path))));
            File.WriteAllBytes(path, originalEntry);

            var execution = new PresetEditExecution(follow); var midi = new FakeEditorMidi { Scope = execution.Scope };
            SavedCapture saved = baseline, live = baseline; var uploaded = new List<byte>();
            midi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref live, uploaded);
            var context = new DeviceContext(d, "99.07", "synthetic"); string receipt = Path.Combine(root, "execution");
            await using (var session = new ReadSession(midi))
                Need((await execution.RunAsync(session, context, receipt, TimeSpan.FromSeconds(1), default, TimeSpan.Zero)).Complete, "Swap uses ordinary verified Save/Reload");
            var replay = new PresetEditExecution(follow); midi = new() { Scope = replay.Scope }; uploaded.Clear();
            midi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref live, uploaded);
            await using (var session = new ReadSession(midi))
                await RejectAsync<InvalidDataException>(() => replay.RunAsync(session, context, Path.Combine(root, "replay"), TimeSpan.FromSeconds(1), default, TimeSpan.Zero));
            Need(midi.Sent.All(SysexProtocol.IsReadRequest), "Old swap plan cannot swap again after saved completion");
            var undo = PresetEditExecution.PrepareUndo(receipt, PresetBody.Sha256(File.ReadAllBytes(Path.Combine(receipt, "attempt.json"))));
            midi = new() { Scope = undo.Execution.Scope }; uploaded.Clear();
            midi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref live, uploaded);
            await using (var session = new ReadSession(midi))
                Need((await undo.Execution.RunAsync(session, context, Path.Combine(root, "undo"), TimeSpan.FromSeconds(1), default, TimeSpan.Zero)).Complete, "Swap exact-result undo");
            Need(saved.Sysex.SequenceEqual(baseline.Sysex), "Swap undo restores complete original bytes");
            if (destination != null)
            {
                string fixtures = Path.Combine(destination, $"Templates-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(fixtures);
                SnapshotExport.Write(Path.Combine(fixtures, "export"), context, [baseline]);
                File.WriteAllBytes(Path.Combine(fixtures, "baseline.syx"), baseline.Sysex);
                File.WriteAllText(Path.Combine(fixtures, "import.json"), JsonSerializer.Serialize(metadata, PresetEditPlan.RequestJson));
                File.WriteAllText(Path.Combine(fixtures, "apply.json"), JsonSerializer.Serialize(apply, PresetEditPlan.RequestJson));
                File.WriteAllText(Path.Combine(fixtures, "swap.json"), JsonSerializer.Serialize(Request(swap with { Policy = "follow-channel-settings" }), PresetEditPlan.RequestJson));
                File.WriteAllBytes(Path.Combine(fixtures, "copy-expected.syx"), copied.Expected);
                File.WriteAllBytes(Path.Combine(fixtures, "swap-expected.syx"), follow.Expected);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)value);
    private static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action test) where T : Exception { try { test(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task RejectAsync<T>(Func<Task> test) where T : Exception { try { await test(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
