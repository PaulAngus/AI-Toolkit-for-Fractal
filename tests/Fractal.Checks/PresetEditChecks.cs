using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Fractal.Core;

static class PresetEditChecks
{
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        var fixture = AudioRoutingChecks.Build(d); byte[] body = fixture.Body;
        Put(body, 0, 2); // Explicit synthetic saved scene 3.
        var baseline = Fixtures.FromBody(d, d.PresetCapacity - 1, body);
        PresetEditRequest Request(params PresetEditOperation[] ops) => new(1, d.Model, d.PresetCapacity, "99.07",
            baseline.Image.Slot, "save", PresetBody.Sha256(baseline.Sysex), ops);
        PresetEditPlan Plan(params PresetEditOperation[] ops) => PresetEditPlan.Create(Request(ops), baseline.Sysex);
        var name = new PresetEditOperation("rename-scene", Scene: 2, Name: "Synthetic edited");
        var state = new PresetEditOperation("set-scene-block-state", EffectId: 58, Scene: 4, Channel: "C", Bypassed: true);
        var edge = new PresetEditOperation("disconnect", SourceRow: d.GridRows, SourceColumn: 1, DestinationRow: 2, DestinationColumn: 2);
        string field = d.Model == "Axe-Fx III" ? "amp.type" : "amp.drive", unit = d.Model == "Axe-Fx III" ? "model-id" : "0-10";
        // The fixture/read catalogue calls the knob domain normalized-0-10. Keep the expected raw math independent.
        if (d.Model != "Axe-Fx III") unit = EditReadCatalogue.Parameters(d, "99.07").Parameters.Single(p => p.Key == field).Unit;
        var parameter = new PresetEditOperation("set-parameter", EffectId: 58, Channel: "A", Key: field, Value: d.Model == "Axe-Fx III" ? 7 : 5,
            Unit: unit, Policy: d.Model == "Axe-Fx III" ? "preserve-other-fields" : null);
        var plan = Plan(name, state, edge, parameter);
        Need(plan.Executable && plan.Changed, "Combined edit must be executable");
        byte[] expectedBody = body.ToArray();
        expectedBody.AsSpan(4 + 32, 32).Clear(); Encoding.ASCII.GetBytes("Synthetic edited").CopyTo(expectedBody, 4 + 32);
        Put(expectedBody, fixture.Offsets[58] + 4, 2); Put(expectedBody, fixture.Offsets[58] + 20, 1);
        Put(expectedBody, Cell(d, 2, 2) + 2, 1);
        int parameterOffset = fixture.Offsets[58] + (d.Model == "FM9" ? 56 : d.Model == "FM3" ? 48 : 34);
        Put(expectedBody, parameterOffset, d.Model == "Axe-Fx III" ? 7 : 32768);
        var authored = SnapshotExport.DecodeFile(d, plan.Expected);
        Need(authored.Image.Body.SequenceEqual(expectedBody), "Independent complete body expected bytes; unrelated data exact");
        int originalStreamLength = BinaryPrimitives.ReadUInt16LittleEndian(baseline.Image.RawImage.AsSpan(0x4a));
        int authoredStreamLength = BinaryPrimitives.ReadUInt16LittleEndian(authored.Image.RawImage.AsSpan(0x4a));
        Need(authoredStreamLength < originalStreamLength &&
            baseline.Image.RawImage.AsSpan(0x4c + authoredStreamLength, originalStreamLength - authoredStreamLength).ContainsAnyExcept((byte)0) &&
            !authored.Image.RawImage.AsSpan(0x4c + authoredStreamLength, originalStreamLength - authoredStreamLength).ContainsAnyExcept((byte)0),
            "Vacated compressed bytes are zeroed while fixed-position opaque padding remains preserved");
        Need(Plan(parameter, edge, state, name).PlanSha256 == plan.PlanSha256, "Canonical equivalent-operation order");
        var repeat = PresetEditPlan.Create(Request(name, state, edge, parameter) with { BaseSha256 = plan.ExpectedSha256 }, plan.Expected);
        Need(repeat.Executable && !repeat.Changed && repeat.Expected.SequenceEqual(plan.Expected), "Repeated intent preserves wire representation and needs no upload/save");
        Need(!Plan(name, name with { Name = "conflict" }).Executable, "Conflicting assignments not last-write-wins");
        Need(!Plan(name with { Name = new string('x', 32) }).Executable, "Name overflow");
        Need(!Plan(name with { Name = "café" }).Executable, "Encoding refusal without truncation");
        Need(!Plan(parameter with { Unit = "wrong" }).Executable, "Units checked");
        Need(!Plan(parameter with { Value = 100000 }).Executable, "Range checked");
        Need(!Plan(edge with { SourceColumn = 4 }).Executable, "Backward/skip edges rejected");
        Need(!Plan(edge with { SourceRow = 2 }).Executable, "Empty endpoints rejected");
        Need(!Plan(new PresetEditOperation("remove-block", EffectId: 66, Policy: "reconnect")).Executable, "Reconnect needs explicit replacement edges");
        Need(!Plan(name with { EffectId = 58 }).Executable, "Operation-specific unknown fields rejected");
        var splitState = Plan(new PresetEditOperation("set-scene-block-state", EffectId: 58, Scene: 4, Channel: "C"),
            new("set-scene-block-state", EffectId: 58, Scene: 4, Bypassed: true));
        Need(splitState.Executable, "Independent channel and bypass assignments compose");
        var copiedChannel = Plan(new PresetEditOperation("copy-channel", EffectId: 66, Channel: "A", TemplateChannel: "C",
            TemplateCapture: Convert.ToBase64String(baseline.Sysex), TemplateSha256: PresetBody.Sha256(baseline.Sysex), TemplateFirmware: "99.07"));
        Need(copiedChannel.Executable, "Explicit full effect-channel template executable");
        byte[] channelExpected = body.ToArray(); int channelWidth = (d.Model == "FM9" ? 73 : 71) * 2;
        body.AsSpan(fixture.Offsets[66] + 34 + channelWidth * 2, channelWidth).CopyTo(channelExpected.AsSpan(fixture.Offsets[66] + 34, channelWidth));
        Need(SnapshotExport.DecodeFile(d, copiedChannel.Expected).Image.Body.SequenceEqual(channelExpected), "Full channel copied without changing other channels/scenes or next prefix");
        Reject<InvalidDataException>(() => PresetEditPlan.Create(Request(name) with { BaseSha256 = new string('0', 64) }, baseline.Sysex));

        var remove = Plan(new PresetEditOperation("remove-block", EffectId: 70, Policy: "replace-with-shunt", Value: 1026));
        Need(remove.Executable, "Connected removal with shunt executable");
        var removed = SnapshotExport.DecodeFile(d, remove.Expected);
        byte[] removalExpected = body.ToArray(); Put(removalExpected, Cell(d, 3, 2), 1026);
        int interval = fixture.Offsets[70] - 12, length = (23 + (d.Model == "FM9" ? 90 : 88) * 4) * 2;
        removalExpected = removalExpected.Take(interval).Concat(removalExpected.Skip(interval + length)).ToArray();
        Need(removed.Image.Body.SequenceEqual(removalExpected), "Removal preserves all incoming/outgoing routes and other record bytes");
        var insertion = new PresetEditOperation("insert-block", EffectId: 70, Row: 3, Column: 2, Policy: "replace-shunt",
            TemplateCapture: Convert.ToBase64String(baseline.Sysex), TemplateSha256: PresetBody.Sha256(baseline.Sysex), TemplateFirmware: "99.07");
        var restored = PresetEditPlan.Create(Request(insertion) with { BaseSha256 = remove.ExpectedSha256 }, remove.Expected);
        Need(restored.Executable, "Connected template insertion executable");
        var projection = PresetEditProjection.Decode(d, "99.07", restored.Expected);
        Need(projection.Blocks.Single(b => b.EffectId == 70).Record != null && projection.Routing.Edges!.Length == 5, "Template insertion preserves connected graph");
        var reinsert = PresetEditPlan.Create(Request(insertion) with { BaseSha256 = restored.ExpectedSha256 }, restored.Expected);
        Need(!reinsert.Changed && reinsert.Executable, "Existing chosen instance is idempotent");
        var detached = Plan(new PresetEditOperation("remove-block", EffectId: 70, Policy: "disconnect"));
        Need(detached.Executable, "Explicit connected disconnect-removal");
        var detachedBody = SnapshotExport.DecodeFile(d, detached.Expected).Image.Body;
        Need(Word(detachedBody, Cell(d, 1, 3) + 2) == 2 && Word(detachedBody, Cell(d, 3, 2)) == 0, "Only incident edges removed; parallel path kept");
        var shunt = Plan(new PresetEditOperation("insert-shunt", EffectId: 1024, Row: 4, Column: 2),
            new("connect", SourceRow: 1, SourceColumn: 1, DestinationRow: 4, DestinationColumn: 2),
            new("connect", SourceRow: 4, SourceColumn: 2, DestinationRow: 1, DestinationColumn: 3));
        Need(shunt.Executable && PresetEditProjection.Decode(d, "99.07", shunt.Expected).Routing.Edges!.Length == 7, "Explicit parallel shunt path");
        var opaqueBody = body.ToArray(); Put(opaqueBody, Cell(d, 2, 2) + 2, 0x8001);
        var opaque = Fixtures.FromBody(d, baseline.Image.Slot, opaqueBody);
        Need(!PresetEditPlan.Create(Request(edge) with { BaseSha256 = PresetBody.Sha256(opaque.Sysex) }, opaque.Sysex).Executable, "Relevant unknown route bits conflict");
        Need(PresetEditPlan.Create(Request(name) with { BaseSha256 = PresetBody.Sha256(opaque.Sysex) }, opaque.Sysex).Executable, "Unrelated opaque routing does not disable scene edit");

        string root = Path.Combine(Path.GetTempPath(), "fractal-edit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string bundle = Path.Combine(root, "bundle"); plan.WriteBundle(bundle);
            Need(PresetEditPlan.OpenBundle(bundle, plan.PlanSha256).Expected.SequenceEqual(plan.Expected), "Bundle re-resolves full plan");
            File.AppendAllText(Path.Combine(bundle, "plan.json"), " "); Reject<InvalidDataException>(() => PresetEditPlan.OpenBundle(bundle, plan.PlanSha256));
            foreach (string mode in new[] { "save", "edit-buffer" })
            {
                var activePlan = PresetEditPlan.Create(Request(name, state, edge, parameter) with { Persistence = mode }, baseline.Sysex);
                var execution = new PresetEditExecution(activePlan); var midi = new FakeEditorMidi { Scope = execution.Scope };
                SavedCapture saved = baseline, live = baseline; var wireUpload = new List<byte>();
                midi.Reply = (q, m) => Reply(q, m, d, ref saved, ref live, wireUpload);
                await using var session = new ReadSession(midi);
                var result = await execution.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, mode), TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
                Need(result.Complete && live.Image.Body.SequenceEqual(expectedBody), "Runtime verifies independently received authored body");
                Need(midi.Sent.Count(q => q[5] == 1 && q[6] == 0x26) == (mode == "save" ? 1 : 0), "Only one requested Save");
                Need(mode == "save" ? saved.Image.Body.SequenceEqual(expectedBody) : saved.Sysex.SequenceEqual(baseline.Sysex), "Persistence intent and saved preservation");
                await RejectAsync<InvalidOperationException>(() => execution.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, "again"), TimeSpan.FromSeconds(1), default));
            }
            {
                var alternate = Fixtures.WithEquivalentCompression(d, authored);
                Need(!alternate.Image.RawImage.SequenceEqual(authored.Image.RawImage) &&
                    PresetUploadPlan.EquivalentCompression(authored.Image.RawImage, authored.Image.Body, alternate.Image.RawImage, alternate.Image.Body),
                    "Valid alternate compression retains the complete authored body");
                byte[] shorterStream = authored.Image.RawImage.ToArray();
                int compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(shorterStream.AsSpan(0x4a));
                BinaryPrimitives.WriteUInt16LittleEndian(shorterStream.AsSpan(0x4a), (ushort)(compressedLength - 1));
                Need(PresetUploadPlan.EquivalentCompression(authored.Image.RawImage, authored.Image.Body,
                    shorterStream, authored.Image.Body), "Compression length may differ while the decoded body and fixed opaque bytes match");
                shorterStream[0x4c + compressedLength + 1] ^= 1;
                Need(!PresetUploadPlan.EquivalentCompression(authored.Image.RawImage, authored.Image.Body,
                    shorterStream, authored.Image.Body), "Different compressed lengths still preserve fixed-position trailing bytes");
                byte[] changedHeader = alternate.Image.RawImage.ToArray(); changedHeader[8] ^= 1;
                Need(!PresetUploadPlan.EquivalentCompression(authored.Image.RawImage, authored.Image.Body, changedHeader, alternate.Image.Body),
                    "Opaque preset header changes are not compression equivalence");
                byte[] changedTail = alternate.Image.RawImage.ToArray(); changedTail[^1] ^= 1;
                Need(!PresetUploadPlan.EquivalentCompression(authored.Image.RawImage, authored.Image.Body, changedTail, alternate.Image.Body),
                    "Opaque preset trailer changes are not compression equivalence");
                var execution = new PresetEditExecution(plan); var midi = new FakeEditorMidi { Scope = execution.Scope };
                SavedCapture saved = baseline, live = baseline; var upload = new List<byte>();
                midi.Reply = (q, m) =>
                {
                    Reply(q, m, d, ref saved, ref live, upload);
                    if (q[5] == 0x79) live = alternate;
                };
                await using var session = new ReadSession(midi);
                var result = await execution.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, "alternate"),
                    TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
                Need(result.Complete && result.ObservedResultSha256 == PresetBody.Sha256(alternate.Sysex) &&
                    saved.Sysex.SequenceEqual(alternate.Sysex), "Alternate compression saved and receipt binds actual wire image");
                var alternateReceiptDirectory = Path.Combine(root, "alternate");
                var alternateReceiptHash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(alternateReceiptDirectory, "attempt.json")));
                var alternateUndo = PresetEditExecution.PrepareUndo(alternateReceiptDirectory, alternateReceiptHash);
                var undoMidi = new FakeEditorMidi { Scope = alternateUndo.Execution.Scope }; var undoUpload = new List<byte>();
                undoMidi.Reply = (q, m) => Reply(q, m, d, ref saved, ref live, undoUpload);
                await using var undoSession = new ReadSession(undoMidi);
                var undone = await alternateUndo.Execution.RunAsync(undoSession, new(d, "99.07", "synthetic"),
                    Path.Combine(root, "alternate-undo"), TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
                Need(undone.Complete && saved.Sysex.SequenceEqual(baseline.Sysex), "Undo accepts only observed result hash and restores original");
            }
            foreach (string fault in new[] { "unsaved", "late-base", "upload-reject", "wrong-loaded", "late-live", "cancel-upload" })
            {
                var execution = new PresetEditExecution(plan); var midi = new FakeEditorMidi { Scope = execution.Scope };
                SavedCapture saved = baseline, live = baseline; var upload = new List<byte>(); int liveReads = 0; bool uploaded = false;
                byte[] foreignBody = body.ToArray(); foreignBody[^1] ^= 1; var foreign = Fixtures.FromBody(d, baseline.Image.Slot, foreignBody);
                if (fault == "unsaved") live = foreign;
                using var cancel = new CancellationTokenSource();
                midi.Reply = (q, m) =>
                {
                    if (q[5] == 3 && q[6] == 127 && q[7] == 127)
                    {
                        liveReads++;
                        if (fault == "late-base" && liveReads == 1) saved = foreign;
                        if (fault == "late-live" && uploaded && liveReads >= 4) live = foreign;
                    }
                    Reply(q, m, d, ref saved, ref live, upload);
                    if (q[5] == 0x78 && fault == "upload-reject") m.Emit(SysexProtocol.Frame(d.ModelByte, 0x64, 0x78, 5));
                    if (q[5] == 0x78 && fault == "cancel-upload") cancel.Cancel();
                    if (q[5] == 0x79) { uploaded = true; if (fault == "wrong-loaded") live = foreign; }
                };
                await using var session = new ReadSession(midi);
                await RejectAsync<Exception>(() => execution.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, fault), TimeSpan.FromSeconds(1), cancel.Token, TimeSpan.Zero));
                Need(midi.Sent.All(q => q[5] != 1 || q[6] != 0x26), "Failure never followed by Save: " + fault);
                var attempt = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, fault, "attempt.json")));
                Need(attempt.RootElement.GetProperty("status").GetString() == "stopped-uncertain", "Failure checkpoint retained");
            }
            var receiptDirectory = Path.Combine(root, "save");
            var receiptHash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(receiptDirectory, "attempt.json")));
            var undo = PresetEditExecution.PrepareUndo(receiptDirectory, receiptHash);
            {
                var midi = new FakeEditorMidi { Scope = undo.Execution.Scope };
                var saved = SnapshotExport.DecodeFile(d, plan.Expected); var live = saved; var upload = new List<byte>();
                midi.Reply = (q, m) => Reply(q, m, d, ref saved, ref live, upload);
                await using var session = new ReadSession(midi);
                await undo.Execution.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, "undo"), TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
                Need(saved.Sysex.SequenceEqual(baseline.Sysex) && live.Sysex.SequenceEqual(baseline.Sysex), "Exact original backup restored after undo/save/reload");
            }
            {
                var midi = new FakeEditorMidi(); var saved = baseline; var live = SnapshotExport.DecodeFile(d, plan.Expected); var upload = new List<byte>();
                midi.Reply = (q, m) => Reply(q, m, d, ref saved, ref live, upload);
                await using var session = new ReadSession(midi);
                var report = JsonSerializer.SerializeToElement(await new PresetEditExecution(plan).ReconcileAsync(session, new(d, "99.07", "synthetic"), TimeSpan.FromSeconds(1), default));
                Need(report.GetProperty("saved").GetString() == "original" && report.GetProperty("loaded").GetString() == "result", "Query-only reconciliation distinguishes unsaved result");
                Need(midi.Sent.All(SysexProtocol.IsReadRequest), "Reconciliation never replays edits");
            }
            {
                var staleUndo = PresetEditExecution.PrepareUndo(receiptDirectory, receiptHash).Execution;
                var midi = new FakeEditorMidi { Scope = staleUndo.Scope }; var saved = baseline; var live = baseline; var upload = new List<byte>();
                midi.Reply = (q, m) => Reply(q, m, d, ref saved, ref live, upload);
                await using var session = new ReadSession(midi);
                await RejectAsync<InvalidDataException>(() => staleUndo.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, "stale-undo"), TimeSpan.FromSeconds(1), default));
                Need(midi.Sent.All(SysexProtocol.IsReadRequest), "Stale undo refuses before mutation");
            }
            if (destination != null)
            {
                string output = Path.Combine(destination, $"Edit-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
                plan.WriteBundle(output);
                File.WriteAllText(Path.Combine(output, "plan-sha256.txt"), plan.PlanSha256);
                SnapshotExport.Write(output + "-Export", new(d, "99.07", "synthetic"), [baseline]);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    internal static void Reply(byte[] q, FakeEditorMidi midi, DeviceDefinition d, ref SavedCapture saved, ref SavedCapture live, List<byte> upload)
    {
        switch (q[5])
        {
            case 0x0d:
                byte[] payload = new byte[34]; payload[0] = (byte)(saved.Image.Slot & 127); payload[1] = (byte)(saved.Image.Slot >> 7);
                Encoding.ASCII.GetBytes(live.Image.Name).CopyTo(payload, 2); midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0d, payload)); break;
            case 0x0c: midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0c, 2)); break;
            case 0x0e:
                byte[] sceneName = new byte[33]; sceneName[0] = q[6]; Encoding.ASCII.GetBytes(live.Image.SceneNames[q[6]]).CopyTo(sceneName, 1);
                midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0e, sceneName)); break;
            case 0x1f:
                foreach (var frame in Fixtures.LiveTable(d, SceneMidiTable.SavedFields(d, PresetBody.Decode(d, live.Image).BlockRecords))) midi.Emit(frame);
                break;
            case 3:
                byte[] wire = (q[6] == 127 && q[7] == 127 ? live : saved).Sysex.ToArray();
                if (q[6] == 127 && q[7] == 127) { wire[6] = 127; wire[7] = 127; wire[11] = SysexProtocol.ComputeChecksum(wire.AsSpan(0, 11)); }
                midi.Emit(wire); break;
            case 0x77: upload.Clear(); upload.AddRange(q); break;
            case 0x78: upload.AddRange(q); break;
            case 0x79:
                upload.AddRange(q); byte[] uploaded = upload.ToArray(); uploaded[6] = (byte)(saved.Image.Slot >> 7); uploaded[7] = (byte)(saved.Image.Slot & 127);
                uploaded[11] = SysexProtocol.ComputeChecksum(uploaded.AsSpan(0, 11)); live = SnapshotExport.DecodeFile(d, uploaded); break;
            case 1: if (q[6] == 0x26) saved = live; if (q[6] == 0x27) live = saved; break;
        }
    }
    private static int Cell(DeviceDefinition d, int row, int col) => 0x104 + ((col - 1) * d.GridRows + row - 1) * 4;
    private static int Word(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset));
    private static void Put(byte[] b, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), (ushort)value);
    private static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
