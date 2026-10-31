using System.Buffers.Binary;
using System.Text.Json;
using Fractal.Core;

static class CalibrationModifierChecks
{
    static void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or FormatException) { return; } throw new Exception("Expected invalid calibration."); }
    static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)value);
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        var fixture = AudioRoutingChecks.Build(d); byte[] body = fixture.Body; int slot = d.PresetCapacity - 1; Put(body, 0, 2);
        int modifier = fixture.Offsets[2] + 34, target = fixture.Offsets[66] + 100, ignore = fixture.Offsets[58] + 120;
        Put(body, modifier, 14); Put(body, modifier + 16, 66); Put(body, modifier + 18, 7);
        Put(body, modifier + 2, 43210); Put(body, modifier + 26, 1000); Put(body, target, 1000); Put(body, ignore, 0);
        var baseline = Fixtures.FromBody(d, slot, body);
        ParameterCalibration Calibration(string kind, int effect, string? channel, string key, string unit, int offset, params (double Value, int Raw)[] points)
        {
            var samples = points.Select(point =>
            {
                byte[] changed = body.ToArray(); Put(changed, offset, point.Raw); byte[] wire = Fixtures.FromBody(d, slot, changed).Sysex;
                return new CalibrationSample(point.Value, PresetBody.Sha256(wire), Convert.ToBase64String(wire));
            }).ToArray();
            return new(1, d.Model, d.PresetCapacity, "99.07", effect, channel, key, unit, samples, kind);
        }
        var calibration = Calibration("parameter", 66, "A", "reverb.observed_level", "dB", target, (-12, 1000), (-6, 9000), (0, 18000));
        var sceneIgnore = Calibration("scene-ignore", 58, null, "amp.scene_ignore", "boolean", ignore, (0, 0), (1, 1));
        var modifierCalibration = Calibration("modifier", 66, null, "modifier.scale", "display-%", modifier + 26, (10, 1000), (50, 32000), (100, 65534))
            with { ModifierSlot = 1, ExpectedBinding = [14, 66, 7] };
        Need(SavedCalibration.Resolve(calibration).RelativeOffset == 100, "Inferred exact word relative to an independently identified record");
        Need(SavedCalibration.Resolve(modifierCalibration).RelativeOffset == 26, "Modifier witness locates one settings word relative to the selected slot");
        var set = new PresetEditOperation("set-calibrated-parameter", EffectId: 66, Channel: "A", Key: calibration.Key, CalibratedValue: -6, Unit: "dB", Calibration: calibration);
        var modifierSet = new PresetEditOperation("set-calibrated-modifier", ModifierSlot: 1, ExpectedBinding: [14, 66, 7], Key: modifierCalibration.Key,
            CalibratedValue: 50, Unit: "display-%", Calibration: modifierCalibration);
        var toggle = new PresetEditOperation("set-scene-ignore", EffectId: 58, Key: sceneIgnore.Key, Unit: "boolean", Calibration: sceneIgnore, Enabled: true);
        var source = new PresetEditOperation("set-modifier-source", ModifierSlot: 1, SourceId: 15, ExpectedBinding: [14, 66, 7]);
        PresetEditRequest Request(params PresetEditOperation[] ops) => new(1, d.Model, d.PresetCapacity, "99.07", slot, "save", PresetBody.Sha256(baseline.Sysex), ops);
        PresetEditPlan Plan(params PresetEditOperation[] ops) => PresetEditPlan.Create(Request(ops), baseline.Sysex);
        var plan = Plan(set, toggle, source); Need(plan.Executable && plan.Changed, "Compound calibrated/Scene Ignore/modifier plan: " + SnapshotExport.Json(plan.Report()));
        byte[] expected = body.ToArray(); Put(expected, target, 9000); Put(expected, ignore, 1); Put(expected, modifier, 15);
        Need(SnapshotExport.DecodeFile(d, plan.Expected).Image.Body.SequenceEqual(expected), "Exactly three independently located words change; curves, assignments and saved default preserved");
        var repeat = PresetEditPlan.Create(Request(set, toggle, source) with { BaseSha256 = plan.ExpectedSha256 }, plan.Expected);
        Need(repeat.Executable && !repeat.Changed, "Repeated desired values are a no-op, including modifier ownership");
        Need(Plan(source, toggle, set).PlanSha256 == plan.PlanSha256, "Canonical operation order");
        Need(!Plan(set with { CalibratedValue = -3 }).Executable, "No invented interpolation");
        Need(!Plan(set with { Unit = "percent" }).Executable, "Unit mismatch refused");
        Need(!Plan(set with { Channel = "B" }).Executable, "Channel binding");
        Need(!Plan(set, set with { CalibratedValue = 0 }).Executable, "Conflicting intent");
        Need(!Plan(set, set with { Key = "reverb.other_label", CalibratedValue = 0, Calibration = calibration with { Key = "reverb.other_label" } }).Executable, "Aliases cannot assign different values to one saved word");
        Need(!Plan(set with { Calibration = calibration with { Firmware = "99.08" } }).Executable, "Exact firmware context");
        Need(!Plan(toggle with { Enabled = null }).Executable, "Explicit boolean required");
        Need(!Plan(source with { ExpectedBinding = [13, 66, 7] }).Executable, "Stale modifier binding");
        Need(!Plan(source with { SourceId = 16384 }).Executable, "Source field bound");
        var modifierPlan = Plan(modifierSet); var modifierExpected = body.ToArray(); Put(modifierExpected, modifier + 26, 32000);
        Need(modifierPlan.Executable && modifierPlan.Changed && SnapshotExport.DecodeFile(d, modifierPlan.Expected).Image.Body.SequenceEqual(modifierExpected),
            "Modifier exact witness changes only selected curve word");
        Need(!PresetEditPlan.Create(Request(modifierSet) with { BaseSha256 = modifierPlan.ExpectedSha256 }, modifierPlan.Expected).Changed,
            "Repeated exact modifier value is a no-op");
        Need(!Plan(modifierSet with { CalibratedValue = 51 }).Executable, "Modifier interpolation refused");
        Need(!Plan(modifierSet with { ExpectedBinding = [15, 66, 7] }).Executable, "Modifier binding cannot drift");
        Need(!Plan(modifierSet with { Unit = "%" }).Executable, "Modifier witness unit must match");
        Need(!Plan(modifierSet, modifierSet with { CalibratedValue = 100 }).Executable, "Conflicting modifier intents refused");
        Reject(() => SavedCalibration.Resolve(modifierCalibration with { ExpectedBinding = [14, 58, 7] }));
        Reject(() => SavedCalibration.Resolve(Calibration("modifier", 66, null, "modifier.source", "id", modifier, (14, 14), (15, 15))
            with { ModifierSlot = 1, ExpectedBinding = [14, 66, 7] }));
        var changedTarget = body.ToArray(); Put(changedTarget, target + 2, 321);
        byte[] foreignTarget = Fixtures.FromBody(d, slot, changedTarget).Sysex;
        Need(!PresetEditPlan.Create(Request(modifierSet) with { BaseSha256 = PresetBody.Sha256(foreignTarget) }, foreignTarget).Executable,
            "Changed target-block context needs new modifier evidence");
        Reject(() => SavedCalibration.Resolve(calibration with { Samples = calibration.Samples.Select(s => s with { Sha256 = new string('0', 64) }).ToArray() }));
        Reject(() => SavedCalibration.Resolve(calibration with { Samples = calibration.Samples.Take(1).ToArray() }));
        Reject(() => SavedCalibration.Resolve(Calibration("parameter", 66, "A", "reverb.invalid", "dB", fixture.Offsets[66] + 30, (0, 73), (1, 74))));
        Reject(() => SavedCalibration.Resolve(Calibration("parameter", 66, "A", "reverb.invalid", "dB", 4, (0, 0), (1, 1))));
        Reject(() => SavedCalibration.Resolve(sceneIgnore with { Channel = "A" }));
        var known = PresetEditProjection.Decode(d, "99.07", baseline.Sysex).Blocks.Single(b => b.EffectId == 66).Parameters.First(p => p.Channel == "A");
        Reject(() => SavedCalibration.Resolve(calibration with { Key = known.Key, Unit = known.Unit }));
        var changedContext = body.ToArray(); Put(changedContext, target + 2, 321);
        byte[] foreign = Fixtures.FromBody(d, slot, changedContext).Sysex;
        Need(!PresetEditPlan.Create(Request(set) with { BaseSha256 = PresetBody.Sha256(foreign) }, foreign).Executable, "Changed neighboring block context needs new evidence");
        var donorBody = body.ToArray(); Put(donorBody, modifier, 16); Put(donorBody, modifier + 16, 58); Put(donorBody, modifier + 18, 11);
        Put(donorBody, modifier + 2, 12345); var donor = Fixtures.FromBody(d, slot, donorBody);
        var copy = new PresetEditOperation("copy-modifier", ModifierSlot: 2, TemplateModifierSlot: 1, ExpectedBinding: [0, 0, 0],
            TemplateCapture: Convert.ToBase64String(donor.Sysex), TemplateSha256: PresetBody.Sha256(donor.Sysex), TemplateFirmware: "99.07");
        var copied = Plan(copy); Need(copied.Executable && copied.Changed, "Explicit modifier template initializes an existing vacant slot");
        var copyExpected = body.ToArray(); donorBody.AsSpan(modifier, 50).CopyTo(copyExpected.AsSpan(fixture.Offsets[35] + 34, 50));
        Need(SnapshotExport.DecodeFile(d, copied.Expected).Image.Body.SequenceEqual(copyExpected), "Complete explicit curve copied without adjacent prefix or other-slot changes");
        Need(!Plan(copy with { TemplateCapture = Convert.ToBase64String(baseline.Sysex), TemplateSha256 = PresetBody.Sha256(baseline.Sysex) }).Executable, "Duplicate modifier target refused");
        Need(!PresetEditPlan.Create(Request(copy) with { BaseSha256 = copied.ExpectedSha256 }, copied.Expected).Changed, "Modifier template repeat no-op");
        Need(PresetEditProjection.Decode(d, "99.07", plan.Expected).Modifiers![0].SourceId == 15, "Offline modifier projection");
        string root = Path.Combine(Path.GetTempPath(), "fractal-calibrated-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            plan.WriteBundle(Path.Combine(root, "bundle")); Need(PresetEditPlan.OpenBundle(Path.Combine(root, "bundle"), plan.PlanSha256).ExpectedSha256 == plan.ExpectedSha256, "Reopened evidence resolves the same plan");
            var execution = new PresetEditExecution(plan); var midi = new FakeEditorMidi { Scope = execution.Scope };
            var saved = baseline; var live = saved; var upload = new List<byte>();
            midi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref live, upload);
            await using (var session = new ReadSession(midi))
            {
                var result = await execution.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, "execution"), TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
                Need(result.Complete && saved.Image.Body.SequenceEqual(expected), "Shared upload/Save/Reload executes and verifies authored result");
            }
            var undo = PresetEditExecution.PrepareUndo(Path.Combine(root, "execution"), PresetBody.Sha256(File.ReadAllBytes(Path.Combine(root, "execution/attempt.json")))).Execution;
            var undoMidi = new FakeEditorMidi { Scope = undo.Scope }; upload.Clear();
            undoMidi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref live, upload);
            await using (var session = new ReadSession(undoMidi))
                Need((await undo.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, "undo"), TimeSpan.FromSeconds(1), default, TimeSpan.Zero)).Complete && saved.Sysex.SequenceEqual(baseline.Sysex), "Exact original restored by ordinary receipt undo");
            if (destination != null)
            {
                string path = Path.Combine(destination, $"Calibration-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "calibration.json"), SnapshotExport.Json(calibration));
                File.WriteAllText(Path.Combine(path, "scene-ignore.json"), SnapshotExport.Json(sceneIgnore));
                File.WriteAllText(Path.Combine(path, "modifier-calibration.json"), SnapshotExport.Json(modifierCalibration));
                File.WriteAllText(Path.Combine(path, "request.json"), SnapshotExport.Json(Request(set, toggle, source)));
                File.WriteAllBytes(Path.Combine(path, "baseline.syx"), baseline.Sysex);
                File.WriteAllBytes(Path.Combine(path, "expected.syx"), plan.Expected);
                SnapshotExport.Write(Path.Combine(path, "baseline-export"), new(d, "99.07", "synthetic"), [baseline]);
                SnapshotExport.Write(Path.Combine(path, "donor-export"), new(d, "99.07", "synthetic"), [donor]);
                for (int i = 0; i < calibration.Samples.Length; i++)
                    SnapshotExport.Write(Path.Combine(path, $"sample-{i}"), new(d, "99.07", "synthetic"), [SnapshotExport.DecodeFile(d, Convert.FromBase64String(calibration.Samples[i].Capture!))]);
                for (int i = 0; i < sceneIgnore.Samples.Length; i++)
                    SnapshotExport.Write(Path.Combine(path, $"ignore-{i}"), new(d, "99.07", "synthetic"), [SnapshotExport.DecodeFile(d, Convert.FromBase64String(sceneIgnore.Samples[i].Capture!))]);
                for (int i = 0; i < modifierCalibration.Samples.Length; i++)
                    SnapshotExport.Write(Path.Combine(path, $"modifier-{i}"), new(d, "99.07", "synthetic"), [SnapshotExport.DecodeFile(d, Convert.FromBase64String(modifierCalibration.Samples[i].Capture!))]);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
