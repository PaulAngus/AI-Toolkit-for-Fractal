using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Fractal.Core;

static class NetworkNotificationChecks
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or InvalidOperationException) { return; } throw new Exception("Expected refusal."); }
    static async Task RejectAsync(Func<Task> action) { try { await action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or InvalidOperationException or OperationCanceledException) { return; } throw new Exception("Expected refusal."); }
    static byte[] Packet(ushort sequence, params byte[] list)
    {
        byte[] p = [128, 225, (byte)(sequence >> 8), (byte)sequence, 0, 0, 0, 100, 0, 0, 0, 7, (byte)list.Length, .. list]; return p;
    }
    public static async Task Run(DeviceDefinition d)
    {
        // Independent RFC wire vectors: timestamp/delta, running status, segment boundaries, sequence rollover.
        var decoder = new RtpMidiCodec(7);
        var shortEvents = decoder.Decode(Packet(65535, 0xb0, 7, 9, 0, 7, 10, 0, 0xf8));
        Need(shortEvents.Count == 3 && shortEvents[1].Bytes.SequenceEqual(new byte[] { 0xb0, 7, 10 }), "Expanded running status");
        Need(decoder.Decode(Packet(0, 0xf0, 0, 1, 0xf0)).Count == 0 && decoder.Incomplete, "First SysEx segment");
        Need(decoder.Decode(Packet(1, 0xf7, 0x74, d.ModelByte, 0xf7))[0].Bytes.SequenceEqual(new byte[] { 240, 0, 1, 0x74, d.ModelByte, 247 }), "Last segment and sequence wrap");
        Reject(() => decoder.Decode(Packet(3, 0xf8)));
        Reject(() => new RtpMidiCodec(7).Decode(Packet(0, 0xf7, 0xf7)));
        Reject(() => new RtpMidiCodec(7).Decode(Packet(0, 0xf0, 1)));
        Reject(() => new RtpMidiCodec(8).Decode(Packet(0, 0xf8)));
        Reject(() => new RtpMidiCodec(7).Decode(Packet(0, 0xb0, 128, 1)));
        var cancel = new RtpMidiCodec(7); cancel.Decode(Packet(0, 0xf0, 1, 0xf0)); Reject(() => cancel.Decode(Packet(1, 0xf7, 0xf4)));
        var delta = Packet(0, 2, 0xf8); delta[12] |= 32;
        Need(new RtpMidiCodec(7).Decode(delta)[0].Timestamp == 102, "Initial delta time");
        byte[] journal = [.. Packet(0, 0xf8), 0, 0, 0]; journal[12] |= 64;
        Need(new RtpMidiCodec(7).Decode(journal).Count == 1, "Journal boundary accepted without claiming repair");
        ushort sendSequence = 9;
        byte[] outgoing = RtpMidiCodec.Encode([0xb1, 7, 100], 7, ref sendSequence, 100)[0];
        Need(outgoing.SequenceEqual(new byte[] { 128, 225, 0, 9, 0, 0, 0, 100, 0, 0, 0, 7, 128, 3, 0xb1, 7, 100 }), "Independent outbound CC vector");
        byte[] large = [240, .. Enumerable.Repeat((byte)1, 2300), 247]; sendSequence = 0;
        var packets = RtpMidiCodec.Encode(large, 7, ref sendSequence, 100);
        Need(packets.Length == 3 && packets[0][^1] == 240 && packets[1][14] == 247 && packets[2][^1] == 247, "Bounded standard SysEx segmentation");
        var largeDecoder = new RtpMidiCodec(7); Need(packets.SelectMany(p => largeDecoder.Decode(p)).Single().Bytes.SequenceEqual(large), "Complete frame assembly");
        Reject(() => RtpMidiTransport.ParseEndpoint("localhost:5004")); Reject(() => RtpMidiTransport.ParseEndpoint("224.0.0.1:5004"));
        Reject(() => RtpMidiTransport.ParseEndpoint("127.0.0.1:65535"));

        byte[] beat = [240, 0, 1, 0x74, d.ModelByte, 0x10, 247];
        byte[] tune = [240, 0, 1, 0x74, d.ModelByte, 0x11, 9, 2, 64, 247];
        Need(DeviceNotifications.Decode(d, beat).Kind == "tempo-beat" && !SysexProtocol.ValidFrame(beat, d.ModelByte, 16), "Checksumless push exception stays separate");
        var tuner = DeviceNotifications.Decode(d, tune); Need(tuner.Kind == "tuner-pitch" && SnapshotExport.Json(tuner.Value!).Contains("\"cents\": 1"), "Tuner offset binary");
        foreach (var frame in new[] { SysexProtocol.Frame(d.ModelByte, 10, 58, 0, 1), SysexProtocol.Frame(d.ModelByte, 11, 58, 0, 2),
            SysexProtocol.Frame(d.ModelByte, 12, 7), SysexProtocol.Frame(d.ModelByte, 15, 63), SysexProtocol.Frame(d.ModelByte, 19, 58, 0, 64), SysexProtocol.Frame(d.ModelByte, 20, 120, 0) })
            Need(DeviceNotifications.Decode(d, frame).Valid, "Known response notification");
        byte[] wrong = SysexProtocol.Frame(d.ModelByte, 12, 2); wrong[^2] ^= 1;
        Need(!DeviceNotifications.Decode(d, wrong).Valid && !DeviceNotifications.Decode(d, SysexProtocol.Frame(d.ModelByte, 12, 127)).Valid, "Corruption/query value not accepted");
        Need(DeviceNotifications.Decode(d, SysexProtocol.Frame(d.ModelByte, 0x55, 2)).Kind == "unknown-fractal", "Unknown frame retained");
        byte[] sceneName = SysexProtocol.Frame(d.ModelByte, 14, [0, .. new byte[32]]);
        Need(SnapshotExport.Json(DeviceNotifications.Decode(d, sceneName).Value!).Contains("\"preset_slot\": null"), "Scene names never invent preset identity");

        var pushFlood = new FakeMidi { Reply = (q, m) =>
        {
            for (int i = 0; i < 100; i++) m.Emit(beat);
            if (q[5] == 12) m.Emit(SysexProtocol.Frame(d.ModelByte, 12, 0));
            else m.Emit(SysexProtocol.Frame(d.ModelByte, 13, new byte[34]));
        } };
        await using (var reader = new ReadSession(pushFlood))
            Need((await reader.CurrentAsync(new(d, "99.07", "synthetic"), TimeSpan.FromSeconds(1), default)).Scene == 1, "Published pushes do not overflow or satisfy solicited reply queue");

        string root = Path.Combine(Path.GetTempPath(), "fractal-network-test-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var input = new FakeMidi();
            var capture = DeviceNotifications.CaptureAsync(input, d, "99.07", Path.Combine(root, "passive"), TimeSpan.FromSeconds(2), 2, default);
            input.Emit(beat[..3]); input.Emit(beat[3..]); input.Emit(tune);
            using var report = JsonDocument.Parse(SnapshotExport.Json(await capture));
            Need(report.RootElement.GetProperty("complete").GetBoolean() && input.Sent.Count == 0, "Passive fragmented capture with no sends");
            capture = DeviceNotifications.CaptureAsync(input, d, "99.07", Path.Combine(root, "failed"), TimeSpan.FromSeconds(2), 3, default);
            input.Emit(beat); await Task.Delay(20); input.Error(new IOException("Synthetic source failure")); await RejectAsync(async () => await capture);
            Need(File.ReadAllText(Path.Combine(root, "failed/notifications.json")).Contains("\"complete\": false"), "Durable partial notification failure");
            capture = DeviceNotifications.CaptureAsync(input, d, "99.07", Path.Combine(root, "truncated"), TimeSpan.FromMilliseconds(40), 3, default);
            input.Emit(beat[..3]); await RejectAsync(async () => await capture);
            Need(File.ReadAllBytes(Path.Combine(root, "truncated/received.syx")).SequenceEqual(beat[..3]), "Raw incomplete evidence retained");

            await using var peer = new Peer();
            await using var network = await RtpMidiTransport.ConnectAsync(peer.Endpoint);
            await RejectAsync(async () => await RtpMidiTransport.ConnectAsync(peer.Endpoint)); // endpoint ownership
            await RejectAsync(() => network.SendReadAsync(EditorCommand.SelectScene(d, 0, 3).Frame, default));
            await RejectAsync(() => network.SendEditorAsync(EditorCommand.SelectScene(d, 0, 3), default));
            var got = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            network.Received += value => got.TrySetResult(value);
            peer.OnMidi = q => peer.Send(SysexProtocol.Frame(d.ModelByte, 12, 2));
            await network.SendReadAsync(SysexProtocol.CurrentSceneQuery(d), default);
            Need((await got.Task.WaitAsync(TimeSpan.FromSeconds(2)))[6] == 2 && peer.MidiCount == 1, "Real UDP read roundtrip and send guards");
            byte[] feedback = await peer.Feedback.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Need(feedback[8] == 0 && feedback[9] == 9 && feedback[10] == 0 && feedback[11] == 0, "Independent Apple receiver feedback sequence placement");
            var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously); network.Faulted += error => fault.TrySetResult(error);
            peer.SkipSequence(); peer.Send(beat); await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await RejectAsync(() => network.SendReadAsync(SysexProtocol.CurrentSceneQuery(d), default));
            Need(peer.MidiCount == 1, "Network loss permanently prevents another send");

            // Real UDP transport also carries the ordinary fully verified live-control workflow, all models.
            var fixture = AudioRoutingChecks.Build(d); BinaryPrimitives.WriteUInt16LittleEndian(fixture.Body, 0); var saved = Fixtures.FromBody(d, 0, fixture.Body); var loaded = saved;
            int scene = 1; var fake = new FakeEditorMidi();
            fake.Reply = (q, m) =>
            {
                if (q.Length == 9 && q[5] == 12 && q[6] != 127)
                { scene = q[6] + 1; byte[] body = loaded.Image.Body.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)(scene - 1)); loaded = Fixtures.FromBody(d, 0, body); }
                else if (q[5] == 12) m.Emit(SysexProtocol.Frame(d.ModelByte, 12, (byte)(scene - 1)));
                else if (q[5] == 19) m.Emit(SysexProtocol.Frame(d.ModelByte, 19, 58, 0, 64));
                else PresetEditChecks.Reply(q, m, d, ref saved, ref loaded, []);
            };
            var context = new DeviceContext(d, "99.07", "synthetic"); LiveControlContext captured;
            await using (var reader = new ReadSession(fake)) captured = await LiveControlScope.CaptureAsync(reader, context, TimeSpan.FromSeconds(2), default);
            var scope = new LiveControlScope(new(1, captured, "scene", Scene: 3));
            await using var writerPeer = new Peer(); writerPeer.OnMidi = q => fake.SendReadAsync(q, default).GetAwaiter().GetResult();
            fake.Received += writerPeer.Send;
            await using var writer = await RtpMidiTransport.ConnectAsync(writerPeer.Endpoint, scope);
            await using var session = new ReadSession(writer);
            await scope.ExecuteAsync(session, context, Path.Combine(root, "control"), TimeSpan.FromSeconds(3), default);
            Need(scene == 3 && saved.Image.Body.SequenceEqual(fixture.Body), "Network live execution verifies change and preserves saved bytes");
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.Body, 2);
            var baseImage = Fixtures.FromBody(d, 0, fixture.Body); var savedEdit = baseImage; var loadedEdit = baseImage;
            var plan = PresetEditPlan.Create(new(1, d.Model, d.PresetCapacity, "99.07", 0, "save", PresetBody.Sha256(baseImage.Sysex),
                [new("rename-scene", Scene: 2, Name: "Synthetic network")]), baseImage.Sysex);
            var execution = new PresetEditExecution(plan); var upload = new List<byte>(); var editing = new FakeEditorMidi();
            editing.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref savedEdit, ref loadedEdit, upload);
            await using var editPeer = new Peer(); editPeer.OnMidi = q => editing.SendReadAsync(q, default).GetAwaiter().GetResult(); editing.Received += editPeer.Send;
            await using var editTransport = await RtpMidiTransport.ConnectAsync(editPeer.Endpoint, execution.Scope);
            await using var editSession = new ReadSession(editTransport);
            await execution.RunAsync(editSession, context, Path.Combine(root, "edit"), TimeSpan.FromSeconds(3), default, TimeSpan.Zero);
            Need(savedEdit.Sysex.SequenceEqual(plan.Expected) && loadedEdit.Sysex.SequenceEqual(plan.Expected), "UDP full upload, readback, Save and Reload preserve exact expected bytes");
            var disconnected = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously); editTransport.Faulted += error => disconnected.TrySetResult(error);
            editPeer.Exit(); await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await RejectAsync(() => editTransport.SendReadAsync(SysexProtocol.CurrentSceneQuery(d), default));

        }
        finally { Directory.Delete(root, true); }
    }

    sealed class Peer : IAsyncDisposable
    {
        readonly UdpClient control, data; readonly CancellationTokenSource stop = new(); readonly Task[] tasks;
        IPEndPoint? client; RtpMidiCodec? decoder; ushort sequence = 9; uint initiator; public int MidiCount; public Action<byte[]>? OnMidi;
        public TaskCompletionSource<byte[]> Feedback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Endpoint => "127.0.0.1:" + ((IPEndPoint)control.Client.LocalEndPoint!).Port;
        public Peer()
        {
            while (true)
            {
                control = new(new IPEndPoint(IPAddress.Loopback, 0));
                try { data = new(new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)control.Client.LocalEndPoint!).Port + 1)); break; }
                catch (SocketException) { control.Dispose(); }
            }
            tasks = [Loop(control, false), Loop(data, true)];
        }
        async Task Loop(UdpClient socket, bool midi)
        {
            try
            {
                while (true)
                {
                    var received = await socket.ReceiveAsync(stop.Token); byte[] b = received.Buffer;
                    if (b[0] == 255 && b[2] == 'I' && b[3] == 'N')
                    {
                        if (midi) { initiator = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(8)); client = received.RemoteEndPoint; decoder = new(BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(12))); }
                        b[2] = (byte)'O'; b[3] = (byte)'K'; BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), 7); await socket.SendAsync(b, received.RemoteEndPoint);
                    }
                    else if (midi && b[0] == 255 && b[2] == 'C' && b[8] == 0)
                    {
                        b[8] = 1; BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), 7); b.AsSpan(12, 8).CopyTo(b.AsSpan(20)); await socket.SendAsync(b, received.RemoteEndPoint);
                    }
                    else if (!midi && b.Length == 12 && b[0] == 255 && b[2] == 'R' && b[3] == 'S') Feedback.TrySetResult(b);
                    else if (midi && b[0] == 128)
                        foreach (var message in decoder!.Decode(b)) { Interlocked.Increment(ref MidiCount); OnMidi?.Invoke(message.Bytes); }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        public void SkipSequence() => sequence++;
        public void Exit()
        {
            byte[] exit = [255, 255, (byte)'B', (byte)'Y', 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 7];
            BinaryPrimitives.WriteUInt32BigEndian(exit.AsSpan(8), initiator); data.Send(exit, client!);
        }
        public void Send(byte[] message)
        {
            uint timestamp = (uint)(System.Diagnostics.Stopwatch.GetTimestamp() * (10000.0 / System.Diagnostics.Stopwatch.Frequency));
            foreach (var frame in new SysexAssembler().Feed(message))
                foreach (var packet in RtpMidiCodec.Encode(frame, 7, ref sequence, timestamp)) data.Send(packet, client!);
        }
        public async ValueTask DisposeAsync() { stop.Cancel(); await Task.WhenAll(tasks); control.Dispose(); data.Dispose(); stop.Dispose(); }
    }
}
