using System.Buffers.Binary;
using Fractal.Core;

static class BuiltinScaleChecks
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)value);
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        var catalogue = EditReadCatalogue.Parameters(d, "32.02").BuiltinScales!;
        Need(EditReadCatalogue.Capabilities(d, "32.02").ExecutionSupported, "Existing supported operations remain enabled");
        if (d.Model != "Axe-Fx III")
        {
            Need(catalogue.Fields.Length == 0 && catalogue.ModifierPercentFields.Count == 0 &&
                catalogue.ModifierNormalizedFields.Count == 0 && catalogue.ModifierRawSelectors.Count == 0,
                "Axe-Fx evidence is never borrowed across models");
            Need(EditReadCatalogue.Parameters(d, "32.02").Parameters.Any(f => f.Key == "amp.drive" && f.WriteSupported), "Own-model supported setters remain enabled");
            return;
        }
        Need(catalogue.Fields.Length == 59 && catalogue.ModifierPercentFields.Count == 7 &&
            catalogue.ModifierNormalizedFields.Count == 4 && catalogue.ModifierRawSelectors.Count == 1,
            "Contained built-in coverage");
        catalogue.ModifierPercentFields["start"] = 8; catalogue.ModifierNormalizedFields["scale"] = 8;
        catalogue.ModifierRawSelectors["damping_type"] = 8; catalogue.Fields[0].EffectIds[0] = 999;
        var freshCatalogue = BuiltinScales.Catalogue(d,"32.02");
        Need(freshCatalogue.ModifierPercentFields["start"] == 3 && freshCatalogue.ModifierNormalizedFields["scale"] == 13 &&
            freshCatalogue.ModifierRawSelectors["damping_type"] == 24 && freshCatalogue.Fields[0].EffectIds[0] == 58,
            "Returned catalogue cannot mutate subsequent write addresses");
        Need(BuiltinScales.Catalogue(d, "32.03").Fields.Length == 0, "No unproven firmware expansion");
        byte[] body = new byte[18000]; Put(body, 0, 2); int pos = 0x400; var offsets = new Dictionary<int, int>();
        void Record(int id, int columns, int rows)
        {
            offsets[id] = pos; Put(body, pos - 12, id); Put(body, pos + 30, columns); Put(body, pos + 32, rows);
            for (int i = 0; i < columns * rows; i++) Put(body, pos + 34 + i * 2, 32767);
            pos += (23 + columns * rows) * 2;
        }
        Record(2, 25, 1); Record(35, 25, 1);
        // Independent shapes and IDs from the source's whole-block census; not assembled from production descriptors.
        int column = 0;
        foreach (var (id, count) in new[] { (58,142), (66,71), (70,89), (46,37), (54,33), (146,19), (154,37), (78,29), (82,33), (118,44), (138,35), (90,35) })
        { Record(id, count, 4); Put(body, 0x104 + column++ * d.GridRows * 4, id); }
        int mod = offsets[2] + 34; Put(body, mod, 14); Put(body, mod + 16, 70); Put(body, mod + 18, 4);
        Put(body, mod + 24 * 2, 1); // Raw damping selector index; no invented enum label.
        int vacant = offsets[35] + 34; Put(body, vacant, 0); Put(body, vacant + 16, 0); Put(body, vacant + 18, 0);
        var baseline = Fixtures.FromBody(d, d.PresetCapacity - 1, body);
        PresetEditOperation Set(int effect, string key, double value, string unit, string channel = "A") => new("set-scaled-parameter", EffectId: effect, Channel: channel, Key: key, PhysicalValue: value, Unit: unit);
        PresetEditOperation Curve(string key, double value, string unit = "%") => new("set-modifier-parameter", ModifierSlot: 1, ExpectedBinding: [14,70,4], Key: "modifier." + key, PhysicalValue: value, Unit: unit);
        PresetEditRequest Request(params PresetEditOperation[] operations) => new(1,d.Model,d.PresetCapacity,"32.02",baseline.Image.Slot,"save",PresetBody.Sha256(baseline.Sysex),operations);
        PresetEditPlan Plan(params PresetEditOperation[] operations) => PresetEditPlan.Create(Request(operations),baseline.Sysex);
        var operations = new[] { Set(58,"amp.level",-15,"dB"), Set(70,"delay.time",7200,"ms"), Set(66,"reverb.time",3,"s"),
            Set(118,"drive.bit_reduce",12,"bits"), Set(138,"megatap.number_of_taps",32,"taps"),
            Set(146,"gate.hold_time",100,"ms"), Set(54,"peq.frequency_3",1000,"Hz"), Set(90,"phaser.tone",5,"bipolar-10","D"),
            Curve("start",25), Curve("mid",75), Curve("end",0), Curve("slope",100), Curve("minimum",75), Curve("maximum",-75),
            Curve("attack",0,"saved-normalized"), Curve("scale",0.25,"saved-normalized"),
            Curve("offset",1,"saved-normalized"), Curve("release",1,"saved-normalized") };
        var plan = Plan(operations); Need(plan.Executable, "Built-in compound plan: " + SnapshotExport.Json(plan.Report()));
        byte[] expected = body.ToArray();
        foreach (var (id,pid,ch,raw) in new[] { (58,21,0,42597), (70,2,0,29490), (66,1,0,1902), (118,16,0,12), (138,3,0,32), (146,2,0,43689), (54,2,0,32767), (90,19,3,49151) })
        { int cols = id == 90 ? 35 : 0; Put(expected, offsets[id] + 34 + 2 * (pid + ch * cols), raw); }
        foreach (var (word,raw) in new[] { (3,16384), (4,49151), (5,0), (6,65534), (1,57342), (2,8192),
            (7,0), (13,16384), (14,65534), (15,65534) }) Put(expected, mod + word * 2, raw);
        Need(SnapshotExport.DecodeFile(d,plan.Expected).Image.Body.SequenceEqual(expected), "Independent literal words; all other settings, channels, routing and modifier bindings preserved");
        var repeat = PresetEditPlan.Create(Request(operations) with { BaseSha256 = plan.ExpectedSha256 },plan.Expected);
        Need(repeat.Executable && !repeat.Changed, "Named operations are idempotent");
        var curveAndSource = new[] { Curve("start",25), new PresetEditOperation("set-modifier-source",ModifierSlot:1,SourceId:15,ExpectedBinding:[14,70,4]) };
        var changedSource = Plan(curveAndSource);
        var repeatedSource = PresetEditPlan.Create(Request(curveAndSource) with { BaseSha256 = changedSource.ExpectedSha256 },changedSource.Expected);
        Need(changedSource.Executable && repeatedSource.Executable && !repeatedSource.Changed,"Curve and explicit source change compose and repeat without overwriting another binding");
        Need(Plan(operations.Reverse().ToArray()).PlanSha256 == plan.PlanSha256, "Canonical compound order");
        Need(!Plan(Set(58,"amp.level",-81,"dB")).Executable && !Plan(Set(54,"peq.frequency_3",0,"Hz")).Executable, "Physical and log-domain bounds");
        Need(!Plan(Set(118,"drive.bit_reduce",12.5,"bits")).Executable &&
            !Plan(Set(118,"drive.bit_reduce",25,"bits")).Executable &&
            !Plan(Set(138,"megatap.number_of_taps",0,"taps")).Executable &&
            !Plan(Set(138,"megatap.number_of_taps",129,"taps")).Executable,
            "Positive integer saved words require exact whole numbers within their source ranges");
        Need(!Plan(Set(118,"drive.bit_reduce",12,"taps")).Executable,
            "Integer units cannot be substituted");
        Need(!Plan(Set(58,"amp.level",0,"%")).Executable, "Units cannot be substituted");
        Need(!Plan(Curve("attack",5,"ms")).Executable && !Plan(Curve("scale",2,"saved-normalized")).Executable &&
            !Plan(Curve("offset",0.25,"%")).Executable, "No guessed display or damping-time scale; normalized bounds and units checked");
        Need(!Plan(Curve("damping_type",1,"saved-normalized")).Executable, "Raw damping selector is read-only without proven enum choices");
        Need(!Plan(Curve("start",25) with { ExpectedBinding = [14,70,6] }).Executable, "Ownership mismatch refused");
        Need(!Plan(Curve("start",25),Curve("start",75)).Executable, "Conflicting curve assignments refused");
        Need(!Plan(Set(70,"delay.time",500,"ms"),Set(70,"delay.time",600,"ms")).Executable, "Conflicting physical assignments refused");
        Need(!PresetEditPlan.Create(Request(operations) with { Firmware = "32.03" },baseline.Sysex).Executable, "Exact firmware proof required only for new fields");
        var decoded = PresetEditProjection.Decode(d,"32.02",plan.Expected);
        Need(decoded.Blocks.Single(b=>b.EffectId==118).Parameters.Single(p=>p.Key=="drive.bit_reduce"&&p.Channel=="A").Value==12 &&
            decoded.Blocks.Single(b=>b.EffectId==138).Parameters.Single(p=>p.Key=="megatap.number_of_taps"&&p.Channel=="A").Value==32,
            "Integer raw words decode without normalization");
        Need(decoded.Modifiers![0].NamedFields!.Single(f => f.Key == "modifier.minimum").Value > 74.99, "Modifier target physical units and reversed range retained");
        Need(decoded.Modifiers[0].NamedFields!.Single(f => f.Key == "modifier.scale").Value is > 0.249 and < 0.251 &&
            decoded.Modifiers[0].RawSelectors!.Single(f => f.Key == "modifier.damping_type").RawValue == 1,
            "Saved fraction and unlabelled damping selector are decoded without inventing display semantics");
        var integerBindingBody = body.ToArray(); Put(integerBindingBody, mod + 16, 118); Put(integerBindingBody, mod + 18, 16);
        var integerBinding = Fixtures.FromBody(d, baseline.Image.Slot, integerBindingBody);
        var integerModifier = new PresetEditOperation("set-modifier-parameter", ModifierSlot: 1, ExpectedBinding: [14,118,16],
            Key: "modifier.minimum", PhysicalValue: 12, Unit: "bits");
        var integerModifierRequest = Request(integerModifier) with { BaseSha256 = PresetBody.Sha256(integerBinding.Sysex) };
        Need(!PresetEditPlan.Create(integerModifierRequest,integerBinding.Sysex).Executable &&
            !PresetEditProjection.Decode(d,"32.02",integerBinding.Sysex).Modifiers![0].NamedFields!.Any(f => f.Key == "modifier.minimum"),
            "Integer target Min/Max words remain unqualified despite direct field support");
        var donorBody = body.ToArray(); Put(donorBody, mod + 24 * 2, 0);
        var donor = Fixtures.FromBody(d, baseline.Image.Slot, donorBody);
        var copySelector = new PresetEditOperation("copy-modifier-selector", ModifierSlot: 1, ExpectedBinding: [14,70,4],
            Key: "modifier.damping_type", TemplateModifierSlot: 1, TemplateCapture: Convert.ToBase64String(donor.Sysex),
            TemplateSha256: PresetBody.Sha256(donor.Sysex), TemplateFirmware: "32.02");
        var selectorPlan = Plan(copySelector);
        Need(selectorPlan.Executable && selectorPlan.Changed && SnapshotExport.DecodeFile(d, selectorPlan.Expected).Image.Body.SequenceEqual(donorBody),
            "Exact donor selector word copied while complete unrelated body remains unchanged");
        Need(!PresetEditPlan.Create(Request(copySelector) with { BaseSha256 = selectorPlan.ExpectedSha256 }, selectorPlan.Expected).Changed,
            "Repeated selector copy is a no-op");
        Need(!Plan(copySelector with { ExpectedBinding = [14,70,5] }).Executable &&
            !Plan(copySelector with { TemplateSha256 = new string('0',64) }).Executable &&
            !Plan(copySelector with { TemplateFirmware = "32.03" }).Executable &&
            !Plan(copySelector with { Key = "modifier.unknown" }).Executable,
            "Selector witness requires exact ownership, bytes, firmware and named field");
        ParameterCalibration Witness(string key, int word)
        {
            var samples = new[] { (Label: 50d, Raw: 32767), (Label: 70d, Raw: 45000) }.Select(point =>
            {
                var bytes = body.ToArray(); Put(bytes, mod + word * 2, point.Raw);
                var wire = Fixtures.FromBody(d, baseline.Image.Slot, bytes).Sysex;
                return new CalibrationSample(point.Label, PresetBody.Sha256(wire), Convert.ToBase64String(wire));
            }).ToArray();
            return new(1, d.Model, d.PresetCapacity, "32.02", 70, null, key, "display-%", samples, "modifier", 1, [14, 70, 4]);
        }
        var displayWitness = Witness("modifier.scale", 13);
        Need(SavedCalibration.Resolve(displayWitness).RelativeOffset == 26, "Display witness agrees with source-scoped modifier word");
        var displaySet = new PresetEditOperation("set-calibrated-modifier", ModifierSlot: 1, ExpectedBinding: [14,70,4],
            Key: "modifier.scale", Unit: "display-%", CalibratedValue: 70, Calibration: displayWitness);
        var displayPlan = Plan(displaySet); var displayExpected = body.ToArray(); Put(displayExpected, mod + 26, 45000);
        Need(displayPlan.Executable && SnapshotExport.DecodeFile(d, displayPlan.Expected).Image.Body.SequenceEqual(displayExpected),
            "Exact caller-labelled display value changes only the source-mapped word");
        Need(!Plan(displaySet with { Calibration = Witness("modifier.offset", 13), Key = "modifier.offset" }).Executable &&
            !Plan(displaySet with { Calibration = Witness("modifier.unmapped", 13), Key = "modifier.unmapped" }).Executable,
            "Source-mapped modifier word cannot be relabelled");
        var sentinel = body.ToArray(); Put(sentinel,offsets[66]+36,65535);
        Need(PresetEditProjection.Decode(d,"32.02",Fixtures.FromBody(d,0,sentinel).Sysex).Blocks.Single(b=>b.EffectId==66).Parameters.Single(p=>p.Key=="reverb.time"&&p.Channel=="A").Value==null,"Unknown raw sentinel retained without coercion");
        Put(sentinel,offsets[118]+34+16*2,65535);
        Need(PresetEditProjection.Decode(d,"32.02",Fixtures.FromBody(d,0,sentinel).Sysex).Blocks.Single(b=>b.EffectId==118).Parameters.Single(p=>p.Key=="drive.bit_reduce"&&p.Channel=="A").Value==null,
            "Unknown integer sentinel retained without coercion");
        Put(sentinel,offsets[118]+34+16*2,25); Put(sentinel,offsets[138]+34+3*2,0);
        var invalidIntegers = PresetEditProjection.Decode(d,"32.02",Fixtures.FromBody(d,0,sentinel).Sysex);
        Need(invalidIntegers.Blocks.Single(b=>b.EffectId==118).Parameters.Single(p=>p.Key=="drive.bit_reduce"&&p.Channel=="A").Value==null &&
            invalidIntegers.Blocks.Single(b=>b.EffectId==138).Parameters.Single(p=>p.Key=="megatap.number_of_taps"&&p.Channel=="A").Value==null,
            "Out-of-profile integer words remain raw rather than guessed display values");
        string root = Path.Combine(Path.GetTempPath(),"fractal-scales-"+Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            plan.WriteBundle(Path.Combine(root,"bundle")); Need(PresetEditPlan.OpenBundle(Path.Combine(root,"bundle"),plan.PlanSha256).ExpectedSha256==plan.ExpectedSha256,"Scale bundle reopening");
            var saved=baseline;var loaded=baseline;var upload=new List<byte>();var execution=new PresetEditExecution(plan);
            var midi=new FakeEditorMidi { Scope=execution.Scope, Reply=(q,m)=>PresetEditChecks.Reply(q,m,d,ref saved,ref loaded,upload) };
            await using(var session=new ReadSession(midi)) Need((await execution.RunAsync(session,new(d,"32.02","synthetic"),Path.Combine(root,"execution"),TimeSpan.FromSeconds(1),default,TimeSpan.Zero)).Complete,"Shared Save/Reload execution");
            Need(saved.Image.Body.SequenceEqual(expected),"Saved authored result");
            var undo=PresetEditExecution.PrepareUndo(Path.Combine(root,"execution"),PresetBody.Sha256(File.ReadAllBytes(Path.Combine(root,"execution/attempt.json")))).Execution;
            var undoMidi=new FakeEditorMidi { Scope=undo.Scope, Reply=(q,m)=>PresetEditChecks.Reply(q,m,d,ref saved,ref loaded,upload) };upload.Clear();
            await using(var session=new ReadSession(undoMidi)) Need((await undo.RunAsync(session,new(d,"32.02","synthetic"),Path.Combine(root,"undo"),TimeSpan.FromSeconds(1),default,TimeSpan.Zero)).Complete && saved.Sysex.SequenceEqual(baseline.Sysex),"Exact original-image undo");
            if(destination!=null)
            {
                string folder=Path.Combine(destination,$"Scales-{d.PresetCapacity}");Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"request.json"),SnapshotExport.Json(Request(operations)));
                File.WriteAllBytes(Path.Combine(folder,"baseline.syx"),baseline.Sysex);File.WriteAllBytes(Path.Combine(folder,"expected.syx"),plan.Expected);
                SnapshotExport.Write(Path.Combine(folder,"export"),new(d,"32.02","synthetic"),[baseline]);
                File.WriteAllBytes(Path.Combine(folder,"selector-expected.syx"),selectorPlan.Expected);
                SnapshotExport.Write(Path.Combine(folder,"selector-donor-export"),new(d,"32.02","synthetic"),[donor]);
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
