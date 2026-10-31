using System.Buffers.Binary;
using Fractal.Core;

static class CompoundMidiChecks
{
    public static async Task Run(DeviceDefinition d)
    {
        var fixture = AudioRoutingChecks.Build(d); byte[] body = fixture.Body; Put(body, 0, 2);
        var baseline = Fixtures.FromBody(d, 0, body); byte[] donorBody = body.ToArray();
        int donorShape = fixture.Offsets[78] + (23 + 29 * 4) * 2;
        Put(donorBody, donorShape - 12, 190); Put(donorBody, donorShape + 30, 257); Put(donorBody, donorShape + 32, 1);
        int cell = 0x104 + (d.GridRows + 3) * 4; Put(donorBody, cell, 190);
        int[] fields = Fixtures.DefaultMidiFields();
        for (int i = 0; i < fields.Length; i++) Put(donorBody, donorShape + 34 + i * 2, fields[i]);
        var donor = Fixtures.FromBody(d, 0, donorBody);
        var insert = new PresetEditOperation("insert-block", EffectId: 190, Row: 4, Column: 2, TemplateCapture: Convert.ToBase64String(donor.Sysex),
            TemplateSha256: PresetBody.Sha256(donor.Sysex), TemplateFirmware: "99.07");
        var edit = new PresetEditOperation("set-scene-midi", Scene: 2, MessageSlot: 3, MessageType: "CC", MidiChannel: 2, Controller: 74, Value: 57);
        PresetEditRequest Request(params PresetEditOperation[] ops) => new(1, d.Model, d.PresetCapacity, "99.07", 0, "save", PresetBody.Sha256(baseline.Sysex), ops);
        var plan = PresetEditPlan.Create(Request(insert, edit), baseline.Sysex);
        Need(plan.Executable, SnapshotExport.Json(plan.Report()));
        int start = fixture.Offsets[58] - 12;
        byte[] expected = body.Take(start).Concat(donorBody.AsSpan(donorShape - 12, 560).ToArray()).Concat(body.Skip(start)).ToArray();
        Put(expected, cell, 190);
        int basis = start + 12 + 34;
        Put(expected, basis + 10 * 2, 1); Put(expected, basis + (64 + 10) * 2, 1);
        Put(expected, basis + (128 + 10) * 2, 74); Put(expected, basis + (192 + 10) * 2, 57);
        Need(SnapshotExport.DecodeFile(d, plan.Expected).Image.Body.SequenceEqual(expected), "Combined insertion/configuration exact body and inactive preservation");
        var clear = new PresetEditOperation("set-scene-midi", Scene: 2, MessageSlot: 3, MessageType: "Off", ExpectedRaw: [1, 1, 74, 57]);
        var currentRequest = Request(clear) with { BaseSha256 = plan.ExpectedSha256 };
        var removed = PresetEditPlan.Create(currentRequest, plan.Expected);
        Need(removed.Executable, "Selective owned-message clear");
        Put(expected, basis + 10 * 2, 0);
        Need(SnapshotExport.DecodeFile(d, removed.Expected).Image.Body.SequenceEqual(expected), "Clear changes only type; all other settings retained");
        Need(!PresetEditPlan.Create(currentRequest with { Operations = [clear with { ExpectedRaw = [1, 1, 74, 58] }] }, plan.Expected).Executable, "Later target change refuses ownership mismatch");
        Need(!PresetEditPlan.Create(currentRequest with { Operations = [edit with { Value = 58 }] }, plan.Expected).Executable, "Occupied row needs explicit replacement");
        Need(PresetEditPlan.Create(currentRequest with { Operations = [edit with { Value = 58, Policy = "replace" }] }, plan.Expected).Executable, "Explicit replacement works");
        Need(!PresetEditPlan.Create(currentRequest with { BaseSha256 = removed.ExpectedSha256 }, removed.Expected).Changed, "Repeated clear is no-op");
        string path = Path.Combine(Path.GetTempPath(), "fractal-compound-midi-" + Guid.NewGuid().ToString("N"));
        try
        {
            var execution = new PresetEditExecution(plan); var midi = new FakeEditorMidi { Scope = execution.Scope };
            var saved = baseline; var live = baseline; var upload = new List<byte>();
            midi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref live, upload);
            await using var session = new ReadSession(midi);
            var result = await execution.RunAsync(session, new(d, "99.07", "synthetic"), path, TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
            Need(result.Complete && saved.Sysex.SequenceEqual(plan.Expected) && midi.Sent.Count(q => q[5] == 0x1f) == 2, "Combined transaction verifies independent live table before save and after reload");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        try
        {
            var execution = new PresetEditExecution(plan); var midi = new FakeEditorMidi { Scope = execution.Scope };
            var saved = baseline; var live = baseline; var upload = new List<byte>();
            midi.Reply = (q, m) =>
            {
                if (q[5] == 0x1f) foreach (var frame in Fixtures.LiveTable(d, fields)) m.Emit(frame);
                else PresetEditChecks.Reply(q, m, d, ref saved, ref live, upload);
            };
            await using var session = new ReadSession(midi);
            bool refused = false;
            try { await execution.RunAsync(session, new(d, "99.07", "synthetic"), path, TimeSpan.FromSeconds(1), default, TimeSpan.Zero); }
            catch (InvalidDataException) { refused = true; }
            Need(refused && !midi.Sent.Any(q => q[5] == 1 && q[6] == 0x26) && saved.Sysex.SequenceEqual(baseline.Sysex), "Different independent live table prevents Save despite matching full image");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), (ushort)value);
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
}
