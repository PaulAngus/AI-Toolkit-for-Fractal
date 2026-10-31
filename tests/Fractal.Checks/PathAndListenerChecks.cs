using System.Buffers.Binary;
using System.Text.Json;
using Fractal.Core;

static class PathAndListenerChecks
{
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        var fixture = AudioRoutingChecks.Build(d); byte[] body = fixture.Body.ToArray(); body[0] = 2;
        // Retain every real record but place Amp and Reverb at independent synthetic endpoints.
        body.AsSpan(0x104, d.GridRows * d.GridColumns * 4).Clear();
        int Cell(int row, int col) => 0x104 + ((col - 1) * d.GridRows + row - 1) * 4;
        void Put(byte[] b, int row, int col, int id, int incoming = 0)
        { BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(Cell(row, col)), (ushort)id); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(Cell(row, col) + 2), (ushort)incoming); }
        Put(body, 1, 1, 58); Put(body, 1, 5, 66);
        // An unrelated existing route is byte-for-byte preserved, including a different shunt ID.
        Put(body, d.GridRows, 6, 1029); Put(body, d.GridRows, 7, 1029, 1 << (d.GridRows - 1));
        var capture = Fixtures.FromBody(d, 0, body);
        var request = new AudioPathRequest(1, d.Model, d.PresetCapacity, "99.07", 0, PresetBody.Sha256(capture.Sysex), "save", [new(1, 1), new(1, 5)], 1024);
        var result = AutomaticRouting.Create(request, capture.Sysex);
        Need(result.Plan.Executable && result.AddedShunts == 3 && result.AddedEdges == 4 && result.Path.All(p => p.Row == 1), "Deterministic shortest straight path");
        var expected = body.ToArray();
        for (int col = 2; col <= 4; col++) Put(expected, 1, col, 1024, 1);
        Put(expected, 1, 5, 66, 1);
        Need(SnapshotExport.DecodeFile(d, result.Plan.Expected).Image.Body.SequenceEqual(expected), "Independent expected whole body; only intended shunts and incoming masks changed");
        var repeat = AutomaticRouting.Create(request with { BaseSha256 = result.Plan.ExpectedSha256 }, result.Plan.Expected);
        Need(!repeat.Plan.Changed && repeat.Plan.Executable && repeat.AddedEdges == 0 && repeat.AddedShunts == 0, "Repeat intent creates no new route or write");
        var obstacle = body.ToArray(); Put(obstacle, 1, 3, 70);
        var obstacleCapture = Fixtures.FromBody(d, 0, obstacle);
        var detour = AutomaticRouting.Create(request with { BaseSha256 = PresetBody.Sha256(obstacleCapture.Sysex) }, obstacleCapture.Sysex);
        Need(detour.Plan.Executable && detour.Path.Single(p => p.Column == 3).Row == 2 && detour.Path.All(p => p.Column != 3 || p.Row != 1), "Unselected audio block avoided; deterministic row tie break");
        var through = AutomaticRouting.Create(request with { BaseSha256 = PresetBody.Sha256(obstacleCapture.Sysex), Waypoints = [new(1, 1), new(1, 3), new(1, 5)] }, obstacleCapture.Sysex);
        Need(through.Plan.Executable && through.Path.All(p => p.Row == 1), "Caller-selected intermediate audio block included");
        var blocked = body.ToArray(); for (int row = 1; row <= d.GridRows; row++) Put(blocked, row, 3, 190);
        var blockedCapture = Fixtures.FromBody(d, 0, blocked);
        Reject<InvalidDataException>(() => AutomaticRouting.Create(request with { BaseSha256 = PresetBody.Sha256(blockedCapture.Sysex) }, blockedCapture.Sysex));
        Reject<ArgumentException>(() => AutomaticRouting.Create(request with { Waypoints = [new(1, 5), new(1, 1)] }, capture.Sysex));
        Reject<ArgumentException>(() => AutomaticRouting.Create(request with { Waypoints = [new(d.GridRows + 1, 1), new(1, 5)] }, capture.Sysex));
        Reject<InvalidDataException>(() => AutomaticRouting.Create(request with { BaseSha256 = new string('0', 64) }, capture.Sysex));
        var shared = body.ToArray(); Put(shared, 2, 1, 1024); Put(shared, 1, 2, 1027, 2); Put(shared, 1, 3, 1028, 1); Put(shared, 1, 4, 1026, 1);
        var sharedCapture = Fixtures.FromBody(d, 0, shared);
        var isolated = AutomaticRouting.Create(request with { BaseSha256 = PresetBody.Sha256(sharedCapture.Sysex) }, sharedCapture.Sysex);
        Need(isolated.Path.Single(p => p.Column == 2).Row != 1, "Existing shared branch avoided under isolated policy");
        var allowed = AutomaticRouting.Create(request with { BaseSha256 = PresetBody.Sha256(sharedCapture.Sysex), SharingPolicy = "allow-existing" }, sharedCapture.Sysex);
        Need(allowed.Plan.Executable && allowed.AddedShunts == 0 && allowed.Path.All(p => p.Row == 1), "Explicit shared-shunt reuse minimizes new cells");
        Need(BinaryPrimitives.ReadUInt16LittleEndian(SnapshotExport.DecodeFile(d, allowed.Plan.Expected).Image.Body.AsSpan(Cell(1, 2) + 2)) == 3, "Shared incoming cable retained");
        string root = Path.Combine(Path.GetTempPath(), "fractal-path-listener-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var saved = capture; var loaded = capture; var upload = new List<byte>(); var execution = new PresetEditExecution(result.Plan);
            var midi = new FakeEditorMidi { Scope = execution.Scope, Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref loaded, upload) };
            await using (var session = new ReadSession(midi))
                Need((await execution.RunAsync(session, new(d, "99.07", "synthetic"), Path.Combine(root, "execute"), TimeSpan.FromSeconds(2), default, TimeSpan.Zero)).Complete, "Automatic path uses complete-image Save/Reload verifier");
            Need(saved.Image.Body.SequenceEqual(expected), "Executed routing result equals independent image");
            var listen = new ControllerListenRequest(1, d.Model, d.PresetCapacity, "99.07", [new("note", 2, 60, null, new("scene", Scene: 3, Cc: new(16, 17, 2)))]);
            Need(ControllerListener.Decode(0x00643c91) == new MidiInputEvent("note-on", 2, 60, 100), "Independent packed WinMM message layout");
            Need(ControllerListener.Decode(0xf8) == null && ControllerListener.Decode(0x0102cf) == null, "Clock and PC outside input mapping ignored");
            Reject<InvalidDataException>(() => ControllerListener.Decode(0x00803c91));
            Reject<InvalidDataException>(() => ControllerListener.Decode(0x7f));
            foreach (string outcome in new[] { "events", "duration", "cancel", "invalid", "fault", "partial", "overflow" })
            {
                var source = new Input { FloodAtSubscribe = outcome == "overflow" }; using var cancel = new CancellationTokenSource();
                string folder = Path.Combine(root, outcome);
                var pending = ControllerListener.CaptureAsync(source, listen, folder, TimeSpan.FromMilliseconds(outcome == "duration" ? 40 : outcome == "partial" ? 10000 : 2000), 5, cancel.Token);
                if (outcome == "events")
                {
                    foreach (uint packet in new uint[] { 0x00643c91, 0x00643c91, 0x00003c91, 0x00643c91, 0x007f3c81 }) source.Emit(packet);
                }
                else if (outcome == "cancel") cancel.Cancel();
                else if (outcome == "invalid") source.Emit(0x00803c91);
                else if (outcome == "fault") source.Fail(new IOException("Synthetic input failure"));
                else if (outcome == "partial")
                {
                    var checkpointDeadline = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(Path.Combine(folder, "listener.json")) && checkpointDeadline.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        if (pending.IsFaulted) await pending;
                        await Task.Delay(5);
                    }
                    Need(File.Exists(Path.Combine(folder, "listener.json")), "Listener created its initial checkpoint");
                    // An observer may retain the previous checkpoint while the listener publishes its next one.
                    using var heldCheckpoint = new FileStream(Path.Combine(folder, "listener.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    source.Emit(0x00643c91);
                    bool retained = false;
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (wait.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        if (pending.IsFaulted) await pending;
                        // The atomic replacement may briefly remove or lock the path on Windows.
                        try
                        {
                            using var checkpointFile = new FileStream(Path.Combine(folder, "listener.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            using var checkpoint = JsonDocument.Parse(checkpointFile);
                            retained = checkpoint.RootElement.GetProperty("events").GetArrayLength() == 1;
                        }
                        catch (IOException) when (wait.Elapsed < TimeSpan.FromSeconds(5)) { }
                        catch (UnauthorizedAccessException) when (wait.Elapsed < TimeSpan.FromSeconds(5)) { }
                        if (retained) break;
                        await Task.Delay(5);
                    }
                    source.Fail(new IOException("Synthetic failure after retained event"));
                    if (!retained)
                    {
                        await RejectAsync<Exception>(async () => await pending);
                        throw new Exception("Listener did not retain the observation before the test's bounded checkpoint deadline.");
                    }
                }
                if (outcome is "events" or "duration") await pending;
                else await RejectAsync<Exception>(async () => await pending);
                using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "listener.json"))); var value = report.RootElement;
                Need(value.GetProperty("complete").GetBoolean() == (outcome is "events" or "duration") && value.GetProperty("sends").GetInt32() == 0, "Completion versus cancellation/fault retained, listener never sends");
                if (outcome == "events") Need(value.GetProperty("events").EnumerateArray().Count(e => e.GetProperty("target").ValueKind != JsonValueKind.Null) == 2, "Held/release duplicate suppression across live input events");
                if (outcome == "partial") Need(value.GetProperty("events").GetArrayLength() == 1, "Completed observation survives later input failure");
                Need(source.Subscribers == 0, "Listener always releases subscriptions");
            }
            if (destination != null)
            {
                string folder = Path.Combine(destination, $"Path-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "request.json"), SnapshotExport.Json(request));
                File.WriteAllText(Path.Combine(folder, "listener.json"), SnapshotExport.Json(listen));
                File.WriteAllBytes(Path.Combine(folder, "baseline.syx"), capture.Sysex);
                SnapshotExport.Write(Path.Combine(folder, "export"), new(d, "99.07", "synthetic"), [capture]);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    sealed class Input : IMidiInputSource
    {
        private Action<MidiShortPacket>? received;
        public bool FloodAtSubscribe;
        public event Action<MidiShortPacket>? ShortReceived
        {
            add { received += value; if (FloodAtSubscribe) for (int i = 0; i < 257; i++) Emit(0xf8); }
            remove { received -= value; }
        }
        public event Action<Exception>? Faulted;
        public int Subscribers => (received?.GetInvocationList().Length ?? 0) + (Faulted?.GetInvocationList().Length ?? 0);
        public void Emit(uint packet) => received?.Invoke(new(packet, 7));
        public void Fail(Exception error) => Faulted?.Invoke(error);
    }
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
