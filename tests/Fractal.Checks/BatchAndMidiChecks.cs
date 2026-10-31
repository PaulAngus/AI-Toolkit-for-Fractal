using System.Buffers.Binary;
using System.Text.Json;
using Fractal.Core;

static class BatchAndMidiChecks
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        // Independent byte vectors through the typed command boundary for every model.
        for (int channel = 1; channel <= 16; channel++)
        foreach (int value in new[] { 0, 127 })
        {
            foreach (int controller in new[] { 0, 127 })
            {
                var cc = EditorCommand.SendCc(d, 0, new(channel, controller, value));
                Need(cc.Frame.SequenceEqual(new byte[] { (byte)(0xb0 + channel - 1), (byte)controller, (byte)value }), "CC wire bytes unchanged");
            }
            var pc = EditorCommand.Program(d, 0, new("custom", channel, 0, 0, value));
            Need(pc.Frame.SequenceEqual(new byte[] { (byte)(0xc0 + channel - 1), (byte)value }), "PC wire bytes unchanged");
        }
        foreach (var invalid in new[] { new ConfiguredCc(0, 0, 0), new(17, 0, 0), new(1, -1, 0), new(1, 128, 0), new(1, 0, -1), new(1, 0, 128) })
            Reject<ArgumentException>(() => EditorCommand.SendCc(d, 0, invalid));
        foreach (int channel in new[] { 0, 17 })
            Reject<ArgumentException>(() => EditorCommand.Program(d, 0, new("custom", channel, 0, 0, 0)));
        foreach (int program in new[] { -1, 128 })
            Reject<ArgumentException>(() => EditorCommand.Program(d, 0, new("custom", 1, 0, 0, program)));
        var mapping = new MidiPresetMapping("direct", 16, 0, 1, 1);
        var target = new MidiRouteTarget("preset", Slot: 129, PresetMapping: mapping);
        var rules = new[] { new MidiRouteRule("note", 2, 60, null, target), new MidiRouteRule("cc", 2, 10, 127, new("bypass", EffectId: 58, Bypassed: true, Cc: new(16, 17, 99))) };
        var router = new MidiInputRouter(d, "99.07", rules);
        Need(router.Route(new("note-on", 1, 60, 100)) == null, "Input channel isolation");
        Need(router.Route(new("note-on", 2, 60, 100)) == target && router.Route(new("note-on", 2, 60, 99)) == null, "Note presses trigger once until released");
        Need(router.Route(new("note-on", 2, 60, 0)) == null && router.Route(new("note-on", 2, 60, 100)) == target, "Velocity zero releases");
        Need(router.Route(new("note-off", 2, 60, 100)) == null && router.Route(new("note-on", 2, 60, 100)) == target, "Note Off release never triggers");
        Need(router.Route(new("cc", 2, 10, 127))?.Kind == "bypass" && router.Route(new("cc", 2, 10, 127)) == null, "CC repeated value suppressed");
        router.Route(new("cc", 2, 10, 0)); Need(router.Route(new("cc", 2, 10, 127)) != null, "CC rearms on value change");
        Reject<ArgumentException>(() => new MidiInputRouter(d, "99.07", [rules[0], rules[0]]));
        Reject<ArgumentException>(() => new MidiInputRouter(d, "99.07", [rules[1] with { Value = null }]));
        Reject<ArgumentException>(() => mapping.Validate(130));
        (mapping with { Mode = "custom", Bank = 7, Program = 8 }).Validate(130);
        Reject<ArgumentException>(() => new ConfiguredCc(0, 1, 0).Validate());
        Reject<ArgumentException>(() => router.Route(new("note-on", 1, 60, 128)));
        string root = Path.Combine(Path.GetTempPath(), "fractal-batch-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var context = new DeviceContext(d, "99.07", "synthetic");
            foreach (string outcome in new[] { "success", "ignored", "stale-after-bank", "cancel-after-bank", "bank-fault", "retained-scene", "retained-scene-drift" })
            {
                var device = new Device(d, outcome.StartsWith("retained-scene", StringComparison.Ordinal)); var midi = device.Midi;
                LiveControlContext capture;
                await using (var reader = new ReadSession(midi)) capture = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                var scope = new PresetLoadScope(d, "99.07", capture.Current, Convert.FromBase64String(capture.SavedCapture), Convert.FromBase64String(capture.LoadedCapture), device.Saved[129].Sysex, false, mapping);
                midi.Scope = scope; using var cancelled = new CancellationTokenSource();
                var originalReply = midi.Reply!;
                midi.Reply = (q, m) =>
                {
                    if (q.Length == 2 && outcome == "ignored") return;
                    originalReply(q, m);
                    if (q.Length == 3)
                    {
                        if (outcome == "stale-after-bank") device.Saved[129] = Fixtures.FromBody(d, 129, AudioRoutingChecks.Build(d).Body);
                        if (outcome == "cancel-after-bank") cancelled.Cancel();
                        if (outcome == "bank-fault") m.Error(new IOException("Synthetic bank send fault"));
                    }
                    if (q.Length == 2 && outcome == "retained-scene-drift")
                    {
                        byte[] changed = device.Loaded.Image.Body.ToArray(); changed[3000] ^= 1;
                        device.Loaded = Fixtures.FromBody(d, device.Slot, changed);
                    }
                };
                await using var session = new ReadSession(midi); session.AuthorizePresetLoad(scope);
                await session.PreparePresetLoadAsync(context, Timeout, default);
                await RejectAsync<InvalidOperationException>(() => session.SendEditorAsync(scope.Commands[1], Timeout, default));
                async Task Recall()
                {
                    foreach (var command in scope.Commands) await session.SendEditorAsync(command, Timeout, cancelled.Token);
                    await session.VerifyPresetLoadAsync(context, Timeout, cancelled.Token);
                }
                if (outcome is "success" or "retained-scene") await Recall();
                else if (outcome == "cancel-after-bank") await RejectAsync<OperationCanceledException>(Recall);
                else if (outcome == "bank-fault") await RejectAsync<IOException>(Recall);
                else await RejectAsync<InvalidDataException>(Recall);
                var sent = midi.Sent.Where(q => q.Length <= 3).ToArray();
                Need(sent[0].SequenceEqual(new byte[] { 0xbf, 0, 1 }), "Independent bank select bytes");
                Need(sent.Length == (outcome is "success" or "ignored" or "retained-scene" or "retained-scene-drift" ? 2 : 1), "No PC after uncertain bank; no retries");
                if (sent.Length == 2) Need(sent[1].SequenceEqual(new byte[] { 0xcf, 1 }), "Independent raw PC bytes");
                if (outcome == "retained-scene")
                    Need(device.Scene == 3 && device.Loaded.Sysex.SequenceEqual(device.Saved[129].Sysex) &&
                        midi.Sent.Count(q => q.Length == 9 && q[5] == 0x0c && q[6] != 127) == 1,
                        "Retained scene is explicitly aligned once, with complete target loaded");
                if (outcome == "retained-scene-drift")
                    Need(device.Scene == 1 && midi.Sent.All(q => q.Length != 9 || q[5] != 0x0c || q[6] == 127),
                        "Unrelated loaded drift blocks scene alignment");
                Need(device.Saves == 0, "Recall never saves");
                await RejectAsync<InvalidOperationException>(() => session.SendEditorAsync(scope.Commands[0], Timeout, default));
                if (destination != null && outcome == "success")
                {
                    string folder = Path.Combine(destination, $"Recall-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, "current.syx"), Convert.FromBase64String(capture.SavedCapture));
                    File.WriteAllBytes(Path.Combine(folder, "loaded.syx"), Convert.FromBase64String(capture.LoadedCapture));
                    File.WriteAllBytes(Path.Combine(folder, "target.syx"), device.Saved[129].Sysex);
                    object Ref(string file) => new { file, sha256 = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(folder, file))) };
                    File.WriteAllText(Path.Combine(folder, "load-request.json"), SnapshotExport.Json(new { schema_version = 1, model = d.Model, capacity = d.PresetCapacity, firmware = "99.07", current_slot = 0, current_scene = 3, target_slot = 129, discard_changes = false,
                        current_saved = Ref("current.syx"), current_loaded = Ref("loaded.syx"), target_saved = Ref("target.syx"), midi_mapping = mapping }));
                    File.WriteAllText(Path.Combine(folder, "routing.json"), SnapshotExport.Json(new MidiRoutingRequest(1, d.Model, d.PresetCapacity, "99.07", rules, [new("note-on", 2, 60, 100), new("note-off", 2, 60, 0)])));
                }
            }
            foreach (string outcome in new[] { "success", "reencoded", "retained-scene", "reload-scene-reset", "reload-scene-drift", "stale", "second-save-ignored", "cancel", "all-unchanged", "discard", "checkpoint-reader" })
            {
                if (outcome == "checkpoint-reader" && !OperatingSystem.IsWindows()) continue;
                var device = new Device(d, outcome == "retained-scene", outcome is "reload-scene-reset" or "reload-scene-drift", outcome == "reload-scene-drift"); var midi = device.Midi; LiveControlContext capture;
                if (outcome == "discard") { byte[] b = device.Loaded.Image.Body.ToArray(); b[^1] ^= 1; device.Loaded = Fixtures.FromBody(d, 0, b); }
                await using (var reader = new ReadSession(midi)) capture = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                PresetEditPlan Plan(int slot) => PresetEditPlan.Create(new(1, d.Model, d.PresetCapacity, "99.07", slot, "save", PresetBody.Sha256(device.Saved[slot].Sysex),
                    [new("rename-scene", Scene: 2, Name: "Batch result " + slot)]), device.Saved[slot].Sysex);
                var plans = new[] { Plan(1), Plan(129) };
                if (outcome == "all-unchanged") plans = plans.Select(p => PresetEditPlan.Create(new(1, d.Model, d.PresetCapacity, "99.07", p.Slot, "save", p.ExpectedSha256, [new("rename-scene", Scene: 2, Name: "Batch result " + p.Slot)]), p.Expected)).ToArray();
                if (outcome == "all-unchanged") foreach (var p in plans) device.Saved[p.Slot] = SnapshotExport.DecodeFile(d, p.Baseline);
                Reject<InvalidDataException>(() => new PresetEditBatch(capture, [plans[0], plans[0]]));
                if (outcome == "discard") Reject<InvalidDataException>(() => new PresetEditBatch(capture, plans));
                var batch = new PresetEditBatch(capture, plans, outcome == "discard"); midi.Scope = batch.Scope;
                if (destination != null && outcome == "success") batch.WriteBundle(Path.Combine(destination, $"Batch-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"));
                if (outcome == "stale") device.Saved[129] = Fixtures.FromBody(d, 129, AudioRoutingChecks.Build(d).Body);
                device.IgnoreSaveNumber = outcome == "second-save-ignored" ? 2 : 0;
                device.ReencodeUploads = outcome == "reencoded";
                using var cancelled = new CancellationTokenSource(); if (outcome == "cancel") cancelled.Cancel();
                string folder = Path.Combine(root, outcome);
                var held = new HashSet<string>(); var readers = new List<Thread>();
                if (outcome == "checkpoint-reader")
                {
                    var reply = midi.Reply!;
                    midi.Reply = (q, m) =>
                    {
                        foreach (string path in new[] { Path.Combine(folder, "progress.json"), Path.Combine(folder, "item-000", "attempt.json") })
                        {
                            if (!File.Exists(path) || !held.Add(path)) continue;
                            var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            var release = new Thread(() => { try { Thread.Sleep(120); } finally { reader.Dispose(); } });
                            readers.Add(release); release.Start();
                        }
                        reply(q, m);
                    };
                }
                Task Run() => batch.RunAsync(midi, context, folder, Timeout, cancelled.Token, TimeSpan.Zero);
                if (outcome is "stale" or "second-save-ignored" or "reload-scene-drift") await RejectAsync<InvalidDataException>(Run);
                else if (outcome == "cancel") await RejectAsync<OperationCanceledException>(Run);
                else await Run();
                foreach (var reader in readers) Need(reader.Join(TimeSpan.FromSeconds(5)), "Checkpoint reader did not release its file");
                if (outcome == "checkpoint-reader") Need(held.Count == 2, "Batch and individual edit checkpoints were both exercised with real Windows reader locks");
                Need(device.Saves == (outcome is "stale" or "cancel" or "all-unchanged" ? 0 : outcome == "reload-scene-drift" ? 1 : 2), "Exactly one Save per changed target, none for stale/cancelled/unchanged batches");
                Need(device.Saved[0].Sysex.SequenceEqual(Convert.FromBase64String(capture.SavedCapture)), "Untargeted initial saved preset preserved");
                if (outcome == "retained-scene")
                    Need(device.Scene == 3 && midi.Sent.Count(q => q.Length == 9 && q[5] == 0x0c && q[6] != 127) == 1,
                        "Batch aligns a retained scene once before editing, preserving both saved results");
                if (outcome == "reload-scene-reset")
                    Need(device.Scene == 3 && midi.Sent.Count(q => q.Length == 9 && q[5] == 0x0c && q[6] != 127) == 3,
                        "Each post-Save reload and intervening target load aligns the verified saved default");
                if (outcome == "reload-scene-drift")
                    Need(device.Scene == 1 && midi.Sent.All(q => q.Length != 9 || q[5] != 0x0c || q[6] == 127),
                        "Unrelated post-Reload drift blocks scene alignment after Save");
                using var progress = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "progress.json")));
                Need(progress.RootElement.GetProperty("completed").GetArrayLength() == (outcome == "second-save-ignored" ? 1 : outcome is "stale" or "cancel" or "reload-scene-drift" ? 0 : 2), "Durable per-item completion count");
                if (outcome == "all-unchanged") Need(midi.Sent.All(SysexProtocol.IsReadRequest) && device.Slot == 0, "Unchanged batch never navigates or uploads");
                if (outcome == "second-save-ignored")
                {
                    string receipt = Path.Combine(folder, "item-000");
                    _ = PresetEditExecution.PrepareUndo(receipt, PresetBody.Sha256(File.ReadAllBytes(Path.Combine(receipt, "attempt.json"))));
                    int count = midi.Sent.Count; await using var reader = new ReadSession(midi);
                    using var result = JsonDocument.Parse(SnapshotExport.Json(await batch.ReconcileAsync(reader, context, Timeout, default)));
                    Need(result.RootElement.GetProperty("states")[0].GetProperty("state").GetString() == "result-observed" && result.RootElement.GetProperty("states")[1].GetProperty("state").GetString() == "original", "Interrupted batch reconciles completed and original targets");
                    Need(midi.Sent.Skip(count).All(SysexProtocol.IsReadRequest), "Batch reconciliation never writes");
                }
                if (outcome == "reencoded")
                {
                    await using var reader = new ReadSession(midi);
                    using var report = JsonDocument.Parse(SnapshotExport.Json(await batch.ReconcileAsync(reader, context, Timeout, default)));
                    Need(report.RootElement.GetProperty("states").EnumerateArray().All(s =>
                        s.GetProperty("state").GetString() == "result-equivalent"),
                        "Re-encoded completed batch items retain query-only result classification");
                }
                await RejectAsync<InvalidOperationException>(Run);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    sealed class Device
    {
        public readonly FakeEditorMidi Midi = new();
        public readonly Dictionary<int, SavedCapture> Saved = new();
        public SavedCapture Loaded;
        public int Slot, Saves, IgnoreSaveNumber, Scene = 3;
        public bool ReencodeUploads;
        private readonly bool retainSceneOnRecall, resetSceneAfterSave, driftOnReload;
        public Device(DeviceDefinition d, bool retainSceneOnRecall = false, bool resetSceneAfterSave = false, bool driftOnReload = false)
        {
            this.retainSceneOnRecall = retainSceneOnRecall; this.resetSceneAfterSave = resetSceneAfterSave; this.driftOnReload = driftOnReload;
            byte[] body = AudioRoutingChecks.Build(d).Body; body[0] = 2;
            foreach (int slot in new[] { 0, 1, 129 }) Saved[slot] = Fixtures.FromBody(d, slot, body);
            if (retainSceneOnRecall)
            {
                byte[] initial = body.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(initial, 0);
                Saved[0] = Fixtures.FromBody(d, 0, initial); Scene = 1;
            }
            Loaded = Saved[0]; var upload = new List<byte>();
            void Recall(int slot)
            {
                Slot = slot; Loaded = Saved[slot];
                if (resetSceneAfterSave && Saves > 0) Scene = 1;
                if ((retainSceneOnRecall || resetSceneAfterSave && Saves > 0) &&
                    BinaryPrimitives.ReadUInt16LittleEndian(Loaded.Image.Body) != Scene - 1)
                {
                    byte[] selected = Loaded.Image.Body.ToArray();
                    BinaryPrimitives.WriteUInt16LittleEndian(selected, (ushort)(Scene - 1));
                    Loaded = SnapshotExport.DecodeFile(d, Fixtures.FromBody(d, slot, selected).Sysex);
                }
                else Scene = BinaryPrimitives.ReadUInt16LittleEndian(Loaded.Image.Body) + 1;
                if (driftOnReload && Saves > 0)
                {
                    byte[] changed = Loaded.Image.Body.ToArray(); changed[3000] ^= 1;
                    Loaded = SnapshotExport.DecodeFile(d, Fixtures.FromBody(d, slot, changed).Sysex);
                }
            }
            Midi.Reply = (q, m) =>
            {
                if (q.Length == 3) return;
                if (q.Length == 2) { Recall(129); return; }
                if (q[5] == 0x0c)
                {
                    if (q[6] == 127) m.Emit(SysexProtocol.Frame(d.ModelByte, 0x0c, (byte)(Scene - 1)));
                    else
                    {
                        Scene = q[6] + 1;
                        if (BinaryPrimitives.ReadUInt16LittleEndian(Loaded.Image.Body) != Scene - 1)
                        {
                            byte[] selected = Loaded.Image.Body.ToArray();
                            BinaryPrimitives.WriteUInt16LittleEndian(selected, (ushort)(Scene - 1));
                            Loaded = selected.SequenceEqual(Saved[Slot].Image.Body) ? Saved[Slot] :
                                SnapshotExport.DecodeFile(d, Fixtures.FromBody(d, Slot, selected).Sysex);
                        }
                    }
                    return;
                }
                if (q[5] == 19) { m.Emit(SysexProtocol.Frame(d.ModelByte, 19, 58, 0, 64)); return; }
                if (q[5] == 3 && !(q[6] == 127 && q[7] == 127)) { m.Emit(Saved[(q[6] << 7) | q[7]].Sysex); return; }
                if (q[5] == 1 && q[6] == 0x27) { Recall(q[12] | q[13] << 7); return; }
                if (q[5] == 1 && q[6] == 0x26) { Saves++; if (Saves != IgnoreSaveNumber) Saved[Slot] = Loaded; return; }
                var current = Saved[Slot]; PresetEditChecks.Reply(q, m, d, ref current, ref Loaded, upload);
                if (q[5] == 0x79 && ReencodeUploads)
                    Loaded = Fixtures.WithEquivalentCompression(d, Loaded);
            };
        }
    }
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
