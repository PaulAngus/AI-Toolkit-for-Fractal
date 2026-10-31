using System.Buffers.Binary;
using System.Text.Json;
using Fractal.Core;

static class PresetOrganizationChecks
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        string root = Path.Combine(Path.GetTempPath(), "fractal-organization-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var sim = new Device(d); var originals = sim.Saved.ToDictionary(p => p.Key, p => p.Value.Sysex.ToArray());
            SavedPresetGuard[] Captures(params int[] slots) => slots.Select(s => new SavedPresetGuard(s, PresetBody.Sha256(originals[s]), Convert.ToBase64String(originals[s]))).ToArray();
            PresetOrganizationRequest Request(SavedPresetGuard[] captures, params PresetOrganizationOperation[] operations) => new(1, d.Model, d.PresetCapacity, "99.07", captures, operations);
            var rename = new PresetOrganizationPlan(Request(Captures(0), new PresetOrganizationOperation("rename", Slot: 0, Name: "Synthetic renamed")));
            var old = SnapshotExport.DecodeFile(d, originals[0]).Image; var renamed = SnapshotExport.DecodeFile(d, rename.Plans[0].Expected).Image;
            Need(renamed.Name == "Synthetic renamed" && old.Body.SequenceEqual(renamed.Body) && old.RawImage.Length == renamed.RawImage.Length &&
                Enumerable.Range(0, old.RawImage.Length).All(i => i is 4 or 5 or >= 8 and < 40 || old.RawImage[i] == renamed.RawImage[i]), "Independent complete rename footprint");
            foreach (string bad in new[] { new string('x', 32), "bad\nname", "non-ASCII é", "trailing " })
                Reject<ArgumentException>(() => new PresetOrganizationPlan(Request(Captures(0), new PresetOrganizationOperation("rename", Slot: 0, Name: bad))));
            Reject<InvalidDataException>(() => new PresetOrganizationPlan(Request(Captures(0, 1), new PresetOrganizationOperation("copy", SourceSlot: 0, DestinationSlot: 1))));
            Reject<ArgumentException>(() => new PresetOrganizationPlan(Request(Captures(0, 1), new PresetOrganizationOperation("move", SourceSlot: 0, DestinationSlot: 1, Overwrite: true))));
            Reject<ArgumentException>(() => new PresetOrganizationPlan(Request(Captures(0, 1), new PresetOrganizationOperation("reorder", Assignments: [new(0, 1)]))));
            Reject<ArgumentException>(() => new PresetOrganizationPlan(Request(Captures(0), new PresetOrganizationOperation("rename", Slot: 0, Name: "x", SourceSlot: 0))));
            Reject<ArgumentException>(() => new PresetOrganizationPlan(Request(Captures(0), new PresetOrganizationOperation("rename", Slot: 0, Name: "x"), new PresetOrganizationOperation("rename", Slot: 0, Name: "y"))));
            Reject<InvalidDataException>(() => new PresetOrganizationPlan(Request(Captures(0).Select(c => c with { Sha256 = new string('0', 64) }).ToArray(), new PresetOrganizationOperation("rename", Slot: 0, Name: "x"))));
            Reject<InvalidDataException>(() => new PresetOrganizationPlan(Request(Captures(0), new PresetOrganizationOperation("import", DestinationSlot: 0, Overwrite: true,
                Donor: new(d.Model == "FM9" ? "FM3" : "FM9", 512, "99.07", PresetBody.Sha256(originals[1]), Convert.ToBase64String(originals[1]))))));
            foreach (string kind in new[] { "rename", "copy", "move", "swap", "reorder", "import", "overlapping", "no-op" })
            {
                var device = new Device(d);
                var ops = kind switch
                {
                    "rename" => new[] { new PresetOrganizationOperation("rename", Slot: 0, Name: "Synthetic renamed") },
                    "copy" => [new PresetOrganizationOperation("copy", SourceSlot: 0, DestinationSlot: 1, Overwrite: true)],
                    "move" => [new PresetOrganizationOperation("move", SourceSlot: 0, DestinationSlot: 1, Overwrite: true, VacatedPolicy: "replace-from-capture", ReplacementSlot: 2)],
                    "swap" => [new PresetOrganizationOperation("swap", SourceSlot: 0, DestinationSlot: 1)],
                    "reorder" => [new PresetOrganizationOperation("reorder", Assignments: [new(0, 1), new(1, 2), new(2, 0)])],
                    "overlapping" => [new PresetOrganizationOperation("copy", SourceSlot: 0, DestinationSlot: 1, Overwrite: true), new PresetOrganizationOperation("copy", SourceSlot: 1, DestinationSlot: 2, Overwrite: true)],
                    "import" => [new PresetOrganizationOperation("import", DestinationSlot: 1, Overwrite: true, Name: "Synthetic imported", Donor: new(d.Model, d.PresetCapacity, "99.07", PresetBody.Sha256(originals[2]), Convert.ToBase64String(originals[2])))],
                    _ => [new PresetOrganizationOperation("copy", SourceSlot: 0, DestinationSlot: 0)]
                };
                int[] slots = kind is "rename" or "no-op" ? [0] : kind == "import" ? [1] : kind is "move" or "reorder" or "overlapping" ? [0, 1, 2] : [0, 1];
                var request = Request(Captures(slots), ops); var plan = new PresetOrganizationPlan(request);
                foreach (var p in plan.Plans)
                {
                    int source = kind switch { "copy" => 0, "move" => p.Slot == 1 ? 0 : 2, "swap" => p.Slot == 1 ? 0 : 1,
                        "reorder" => p.Slot == 1 ? 0 : p.Slot == 2 ? 1 : 2, "overlapping" => p.Slot == 1 ? 0 : 1, "import" => 2, _ => 0 };
                    var donor = SnapshotExport.DecodeFile(d, originals[source]).Image; var expected = SnapshotExport.DecodeFile(d, p.Expected).Image;
                    Need(expected.Body.SequenceEqual(donor.Body), "Copied full body independently matches immutable donor including names, routing, MIDI and saved scene");
                    if (kind is not ("rename" or "import")) Need(expected.RawImage.SequenceEqual(donor.RawImage), "Complete donor raw header/compression/padding preserved");
                    Need(expected.Slot == p.Slot && p.BeforeScene == BinaryPrimitives.ReadUInt16LittleEndian(SnapshotExport.DecodeFile(d, originals[p.Slot]).Image.Body) + 1, "Destination address and original default scene remain explicit");
                }
                string bundle = Path.Combine(root, kind + "-plan"); plan.WriteBundle(bundle);
                Need(PresetOrganizationPlan.Open(bundle, plan.PlanSha256).PlanSha256 == plan.PlanSha256, "Organization plan independently reopens");
                var context = new DeviceContext(d, "99.07", "synthetic"); LiveControlContext live;
                await using (var reader = new ReadSession(device.Midi)) live = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                var batch = plan.BuildBatch(live); device.Midi.Scope = batch.Scope;
                string execution = Path.Combine(root, kind + "-execution");
                await batch.RunAsync(device.Midi, context, execution, Timeout, default, TimeSpan.Zero);
                foreach (var p in plan.Plans) Need(device.Saved[p.Slot].Sysex.SequenceEqual(p.Expected), "Saved/reloaded organized image equals independent plan");
                Need(device.Saved[3].Sysex.SequenceEqual(originals[3]), "Untargeted complete preset preserved");
                if (kind == "no-op") Need(device.Uploads == 0 && device.Saves == 0 && device.Reloads == 0, "No-op emits reads only");
                else
                {
                    Need(device.Saves == plan.Plans.Count(p => p.Changed), "One Save per changed destination");
                    var current = plan.Plans.Where(p => p.Changed).Select(p => device.Guard(p.Slot)).ToArray();
                    string progressHash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(execution, "progress.json")));
                    string inverseDir = Path.Combine(root, kind + "-undo-plan");
                    var inverseReport = JsonSerializer.SerializeToElement(PresetOrganizationPlan.PlanUndo(execution, progressHash, new(1, d.Model, d.PresetCapacity, "99.07", current), inverseDir), SnapshotExport.JsonOptions);
                    var inverse = PresetOrganizationPlan.Open(inverseDir, inverseReport.GetProperty("plan_sha256").GetString()!);
                    // Later changes to an eligible target are refused; do not revert unrelated later edits.
                    var later = current.ToArray(); var first = SnapshotExport.DecodeFile(d, Convert.FromBase64String(later[0].Capture)).Image;
                    byte[] altered = first.Body.ToArray(); altered[^1] ^= 1;
                    byte[] changed = Fixtures.FromBody(d, first.Slot, altered, first.Name).Sysex;
                    later[0] = new(first.Slot, PresetBody.Sha256(changed), Convert.ToBase64String(changed));
                    Reject<InvalidDataException>(() => PresetOrganizationPlan.PlanUndo(execution, progressHash, new(1, d.Model, d.PresetCapacity, "99.07", later), inverseDir + "-later"));
                    await using (var reader = new ReadSession(device.Midi)) live = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                    var undoBatch = inverse.BuildBatch(live); device.Midi.Scope = undoBatch.Scope;
                    await undoBatch.RunAsync(device.Midi, context, Path.Combine(root, kind + "-undo"), Timeout, default, TimeSpan.Zero);
                    foreach (int slot in slots) Need(device.Saved[slot].Sysex.SequenceEqual(originals[slot]), "Undo restores exact original images at eligible slots");
                    var already = current.Select(c => device.Guard(c.Slot)).ToArray();
                    var repeated = JsonSerializer.SerializeToElement(PresetOrganizationPlan.PlanUndo(execution, progressHash, new(1, d.Model, d.PresetCapacity, "99.07", already), inverseDir + "-repeat"), SnapshotExport.JsonOptions);
                    Need(repeated.GetProperty("plan").GetProperty("changed_count").GetInt32() == 0, "Repeated restoration plans no-op");
                }
                if (destination != null && kind == "reorder")
                {
                    string output = Path.Combine(destination, $"Organization-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(output);
                    plan.WriteBundle(Path.Combine(output, "organization")); File.WriteAllText(Path.Combine(output, "request.json"), JsonSerializer.Serialize(request, PresetOrganizationPlan.JsonOptions));
                    await using (var reader = new ReadSession(device.Midi)) live = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                    File.WriteAllText(Path.Combine(output, "context.json"), SnapshotExport.Json(live)); // original selection/captures restored after undo
                    SnapshotExport.Write(Path.Combine(output, "export"), context, slots.Select(s => SnapshotExport.DecodeFile(d, originals[s])).ToArray());
                    foreach (int slot in slots) File.WriteAllBytes(Path.Combine(output, $"original-{slot}.syx"), originals[slot]);
                    File.WriteAllText(Path.Combine(output, "plan-sha256.txt"), plan.PlanSha256);
                    Copy(execution, Path.Combine(output, "execution"));
                    var resultCaptures = plan.Plans.Where(p => p.Changed).Select(p => SnapshotExport.DecodeFile(d, p.Expected)).ToArray();
                    SnapshotExport.Write(Path.Combine(output, "result-export"), context, resultCaptures);
                }
            }
            foreach (string failure in new[] { "stale-source", "source-after-load", "ignored-second-save", "alignment-drift", "cancel-after-first" })
            {
                var device = new Device(d); var context = new DeviceContext(d, "99.07", "synthetic"); LiveControlContext live;
                await using (var reader = new ReadSession(device.Midi)) live = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                var plan = new PresetOrganizationPlan(Request(Captures(0, 1, 2), new PresetOrganizationOperation("copy", SourceSlot: 0, DestinationSlot: 1, Overwrite: true), new PresetOrganizationOperation("copy", SourceSlot: 0, DestinationSlot: 2, Overwrite: true)));
                var batch = plan.BuildBatch(live); device.Midi.Scope = batch.Scope;
                using var cancelled = new CancellationTokenSource();
                if (failure == "stale-source") device.Alter(0);
                if (failure == "ignored-second-save") device.IgnoreSave = 2;
                device.After = q =>
                {
                    if (failure == "source-after-load" && q[5] == 1 && q[6] == 39) device.Alter(0);
                    if (failure == "alignment-drift" && q[5] == 121) device.AlterLoaded();
                    if (failure == "cancel-after-first" && device.Saves == 1 && q[5] == 1 && q[6] == 39) cancelled.Cancel();
                };
                string execution = Path.Combine(root, failure);
                await RejectAsync<Exception>(() => batch.RunAsync(device.Midi, context, execution, Timeout, cancelled.Token, TimeSpan.Zero));
                Need(File.ReadAllText(Path.Combine(execution, "progress.json")).Contains("stopped-uncertain"), "Failure checkpoint retained");
                if (failure is "stale-source" or "source-after-load" or "alignment-drift") Need(device.Saves == 0, "Stale or unrelated upload change blocks Save");
                if (failure == "ignored-second-save")
                {
                    var proof = JsonSerializer.SerializeToElement(PresetOrganizationPlan.PlanUndo(execution, PresetBody.Sha256(File.ReadAllBytes(Path.Combine(execution, "progress.json"))),
                        new(1, d.Model, d.PresetCapacity, "99.07", [device.Guard(1)]), execution + "-eligible-undo"), SnapshotExport.JsonOptions);
                    Need(proof.GetProperty("eligible_count").GetInt32() == 1, "Only completed first target is eligible after partial failure");
                    var inverse = PresetOrganizationPlan.Open(execution + "-eligible-undo", proof.GetProperty("plan_sha256").GetString()!);
                    await using (var reader = new ReadSession(device.Midi)) live = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                    var undoBatch = inverse.BuildBatch(live, true); device.Midi.Scope = undoBatch.Scope; device.IgnoreSave = 0;
                    await undoBatch.RunAsync(device.Midi, context, execution + "-undo", Timeout, default, TimeSpan.Zero);
                    Need(device.Saved[1].Sysex.SequenceEqual(originals[1]) && device.Saved[2].Sysex.SequenceEqual(originals[2]), "Eligible undo leaves uncertain destination original saved bytes untouched");
                }
                int mutations = device.Saves;
                await using (var reader = new ReadSession(device.Midi))
                {
                    await batch.ReconcileAsync(reader, context, Timeout, default);
                    Need(device.Saves == mutations, "Reconciliation is query-only");
                }
                await RejectAsync<InvalidOperationException>(() => batch.RunAsync(device.Midi, context, execution + "-replay", Timeout, default, TimeSpan.Zero));
            }
            {
                var device = new Device(d) { AdjustUploadScene = true, ResetAfterSave = true };
                var context = new DeviceContext(d, "99.07", "synthetic"); LiveControlContext live;
                await using (var reader = new ReadSession(device.Midi)) live = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                var plan = new PresetOrganizationPlan(Request(Captures(0, 1), new PresetOrganizationOperation("copy", SourceSlot: 0, DestinationSlot: 1, Overwrite: true)));
                var batch = plan.BuildBatch(live); device.Midi.Scope = batch.Scope;
                await batch.RunAsync(device.Midi, context, Path.Combine(root, "both-alignments"), Timeout, default, TimeSpan.Zero);
                Need(device.Scene == 1 && device.Saved[1].Image.Body.SequenceEqual(SnapshotExport.DecodeFile(d, originals[0]).Image.Body) &&
                    device.Midi.Sent.Count(q => q.Length == 9 && q[5] == 12 && q[6] != 127) == 2,
                    "Separate once-only pre-Save and post-Reload alignment preserves donor default scene with complete readback");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    sealed class Device
    {
        readonly DeviceDefinition definition; readonly List<byte> upload = [];
        public readonly FakeEditorMidi Midi = new(); public readonly Dictionary<int, SavedCapture> Saved = [];
        public SavedCapture Loaded; public int Slot, Scene = 1, Saves, Uploads, Reloads, IgnoreSave; public Action<byte[]>? After;
        public bool AdjustUploadScene, ResetAfterSave;
        public Device(DeviceDefinition d)
        {
            definition = d;
            foreach (int slot in Enumerable.Range(0, 4))
            {
                byte[] body = AudioRoutingChecks.Build(d).Body; BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)slot); body[^1] = (byte)(80 + slot);
                Saved[slot] = Fixtures.FromBody(d, slot, body, $"Synthetic organization {slot}");
            }
            Loaded = Saved[0];
            Midi.Reply = (q, m) =>
            {
                if (q[5] == 12)
                {
                    if (q[6] == 127) m.Emit(SysexProtocol.Frame(d.ModelByte, 12, (byte)(Scene - 1)));
                    else
                    {
                        Scene = q[6] + 1; byte[] body = Loaded.Image.Body.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)(Scene - 1));
                        if (!body.SequenceEqual(Loaded.Image.Body)) Loaded = SnapshotExport.DecodeFile(d, PresetBodyWriter.ReplaceBody(d, Loaded.Sysex, body));
                    }
                }
                else if (q[5] == 19) m.Emit(SysexProtocol.Frame(d.ModelByte, 19, 58, 0, 64));
                else if (q[5] == 3 && !(q[6] == 127 && q[7] == 127)) m.Emit(Saved[q[6] << 7 | q[7]].Sysex);
                else if (q[5] == 1 && q[6] == 39)
                {
                    Slot = q[12] | q[13] << 7; Loaded = Saved[Slot]; Scene = ResetAfterSave && Saves > 0 ? 8 : BinaryPrimitives.ReadUInt16LittleEndian(Loaded.Image.Body) + 1;
                    if (ResetAfterSave && Saves > 0) SetLoadedScene(); Reloads++;
                }
                else if (q[5] == 1 && q[6] == 38) { Saves++; if (Saves != IgnoreSave) Saved[Slot] = Loaded; }
                else
                {
                    var current = Saved[Slot]; PresetEditChecks.Reply(q, m, d, ref current, ref Loaded, upload);
                    if (q[5] == 121) { Uploads++; if (AdjustUploadScene) SetLoadedScene(); }
                }
                After?.Invoke(q);
            };
        }
        public SavedPresetGuard Guard(int slot) => new(slot, PresetBody.Sha256(Saved[slot].Sysex), Convert.ToBase64String(Saved[slot].Sysex));
        public void Alter(int slot) { byte[] body = Saved[slot].Image.Body.ToArray(); body[^2] ^= 1; Saved[slot] = Fixtures.FromBody(definition, slot, body, Saved[slot].Image.Name); }
        public void AlterLoaded() { byte[] body = Loaded.Image.Body.ToArray(); body[^2] ^= 1; Loaded = Fixtures.FromBody(definition, Slot, body, Loaded.Image.Name); }
        void SetLoadedScene()
        {
            byte[] body = Loaded.Image.Body.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)(Scene - 1));
            Loaded = SnapshotExport.DecodeFile(definition, PresetBodyWriter.ReplaceBody(definition, Loaded.Sysex, body));
        }
    }
    static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); foreach (string dir in Directory.GetDirectories(source)) Copy(dir, Path.Combine(destination, Path.GetFileName(dir))); }
    static void Need(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
