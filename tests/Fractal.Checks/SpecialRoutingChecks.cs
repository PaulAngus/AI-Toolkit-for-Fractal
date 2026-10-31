using System.Buffers.Binary;
using System.Text.Json;
using Fractal.Core;

static class SpecialRoutingChecks
{
    public static async Task Run(DeviceDefinition d)
    {
        byte[] body = new byte[12000]; Put(body, 0, 2); int pos = 0x400;
        var positions = new Dictionary<int, int>();
        void Record(int id, int columns, int rows)
        { positions[id] = pos; Put(body, pos - 12, id); Put(body, pos + 30, columns); Put(body, pos + 32, rows); pos += (23 + columns * rows) * 2; }
        void Cell(int row, int column, int id, int mask = 0)
        { int offset = 0x104 + ((column - 1) * d.GridRows + row - 1) * 4; Put(body, offset, id); Put(body, offset + 2, mask); }
        Record(2, 25, 1); Record(35, 25, 1);
        // Literal expected model-independent header-type shapes from pinned source, not the production resolver.
        var types = new[] { (46, 37, 58, "compressor.type"), (114, 37, 34, "filter.type"), (82, 33, 34, "flanger.type"),
            (90, 35, 34, "phaser.type"), (106, 22, 34, "tremolo.type"), (94, 25, 34, "wah.type"),
            (122, 12, 34, "enhancer.type"), (86, 21, 34, "rotary.type") };
        int col = 1;
        foreach (var (id, columns, first, _) in types)
        { Record(id, columns, 4); Cell(3, col++, id); for (int ch = 0; ch < 4; ch++) Put(body, positions[id] + first + ch * columns * 2, 100 + ch); }
        foreach (int id in new[] { 37, 43, 38, 42 }) Record(id, 17, 4); // Synthetic I/O payload shape; scene headers only.
        Record(182, 2, 1); Record(186, 6, 1);
        Cell(1, 1, 37); Cell(1, 2, 43, 1); Cell(1, 3, 38, 1); Cell(1, 4, 42, 1);
        Cell(2, 1, 186); Cell(2, 2, 182);
        Put(body, positions[38] + 16, 1); // Scene 2: Input 2 bypassed, grid passes through.
        Put(body, positions[43] + 18, 1); // Scene 3: Output 2 bypassed, grid and output muted.
        if (d.Model != "FM3") { Record(41, 17, 4); Cell(4, 1, 41); }
        var saved = Fixtures.FromBody(d, d.PresetCapacity - 1, body);
        PresetEditRequest Request(params PresetEditOperation[] operations) => new(1, d.Model, d.PresetCapacity, "99.07", saved.Image.Slot,
            "save", PresetBody.Sha256(saved.Sysex), operations);
        var report = PresetEditProjection.Decode(d, "99.07", saved.Sysex);
        Need(report.SpecialRouting!.Links.Single().Source == "cell:2:2" && report.SpecialRouting.Links.Single().Destination == "cell:2:1", "Fixed implicit pair has no cable bits");
        Need(report.SpecialRouting.FeedbackComponents.Length == 0, "Disconnected send has no loop");
        Need(report.SpecialRouting.Ports.Any(p => p.Endpoint == "USB input block") == (d.Model == "Axe-Fx III"), "Only source-qualified USB identity becomes an endpoint");
        var paths = report.SpecialRouting.ScenePaths;
        Need(paths[0].Paths.Single(p => p.Input == "cell:1:1").Outputs.SequenceEqual(new[] { "cell:1:2" }), "Engaged Input replaces prior grid audio");
        Need(paths[1].Paths.Single(p => p.Input == "cell:1:1").Outputs.SequenceEqual(new[] { "cell:1:2", "cell:1:4" }), "Bypassed Input passes grid audio");
        Need(paths[2].Paths.Single(p => p.Input == "cell:1:1").Outputs.Length == 0, "Output bypass mutes tap and downstream grid");
        var through = PresetEditPlan.Create(Request(new PresetEditOperation("connect", SourceRow: 1, SourceColumn: 2, DestinationRow: 1, DestinationColumn: 3)), saved.Sysex);
        Need(through.Executable && !through.Changed, "Documented Output to Input cable is legal and idempotent");
        var edge = new PresetEditOperation("connect", SourceRow: 2, SourceColumn: 1, DestinationRow: 2, DestinationColumn: 2);
        Need(!PresetEditPlan.Create(Request(edge), saved.Sysex).Executable, "New feedback has explicit intent");
        var operations = types.Select(t => new PresetEditOperation("set-block-type", EffectId: t.Item1, Channel: "D", Key: t.Item4,
            Value: 7, Unit: "numeric-id", Policy: "preserve-other-fields")).Append(edge).ToArray();
        var plan = PresetEditPlan.Create(Request(operations) with { FeedbackPolicy = "allow" }, saved.Sysex);
        Need(plan.Executable, SnapshotExport.Json(plan.Report()));
        byte[] expected = body.ToArray(); foreach (var (id, columns, first, _) in types) Put(expected, positions[id] + first + columns * 6, 7);
        Put(expected, 0x104 + (d.GridRows + 1) * 4 + 2, 2);
        Need(SnapshotExport.DecodeFile(d, plan.Expected).Image.Body.SequenceEqual(expected), "All eight mappings and routing touch only independently expected words");
        var final = PresetEditProjection.Decode(d, "99.07", plan.Expected);
        Need(final.SpecialRouting!.FeedbackComponents.Single().SequenceEqual(new[] { "cell:2:1", "cell:2:2" }), "Cycle identified through implicit link");
        Need(PresetEditPlan.Create(Request(new PresetEditOperation("rename-scene", Scene: 1, Name: "Loop")) with { BaseSha256 = plan.ExpectedSha256 }, plan.Expected).Executable,
            "Existing feedback does not disable unrelated edits");
        var repeated = PresetEditPlan.Create(Request(operations) with { BaseSha256 = plan.ExpectedSha256 }, plan.Expected);
        Need(repeated.Executable && !repeated.Changed, "Existing loop idempotence without changing intent");
        byte[] donorBody = body.ToArray(); Put(donorBody, positions[186] + 34, 1234);
        var donor = Fixtures.FromBody(d, saved.Image.Slot, donorBody);
        var copy = PresetEditPlan.Create(Request(new PresetEditOperation("copy-block-settings", EffectId: 186, TemplateCapture: Convert.ToBase64String(donor.Sysex),
            TemplateSha256: PresetBody.Sha256(donor.Sysex), TemplateFirmware: "99.07")), saved.Sysex);
        Need(copy.Executable && SnapshotExport.DecodeFile(d, copy.Expected).Image.Body.SequenceEqual(donorBody), "Single-channel Return settings copy preserves complete other body");
        string folder = Path.Combine(Path.GetTempPath(), "fractal-special-" + Guid.NewGuid().ToString("N"));
        try
        {
            var execution = new PresetEditExecution(plan); var midi = new FakeEditorMidi { Scope = execution.Scope };
            var persisted = saved; var live = saved; var upload = new List<byte>();
            midi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref persisted, ref live, upload);
            await using var session = new ReadSession(midi);
            var result = await execution.RunAsync(session, new(d, "99.07", "synthetic"), folder, TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
            Need(result.Complete && persisted.Image.Body.SequenceEqual(expected), "New mappings and feedback execute through verified Save/Reload");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), (ushort)value);
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
}
