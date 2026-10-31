using Fractal.Core;
using MightBeUseful.Midi;

static class SharedMidiTransportChecks
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    public static async Task Run()
    {
        var d = DeviceDefinition.Create("FM9");
        var input = new Input(); var output = new Output(); bool released = false;
        var midi = new SharedMidiTransport(input, output, releaseOwnership: () => released = true);
        await Reject<InvalidOperationException>(() => midi.SendReadAsync(EditorCommand.SavePreset(d, 2).Frame, default));
        await Reject<InvalidOperationException>(() => midi.SendEditorAsync(EditorCommand.SavePreset(d, 2), default));
        Need(output.Frames.Count == 0, "Read-only authority must reject before package send");
        await midi.SendReadAsync(SysexProtocol.IdentityQuery(), default);
        Need(output.Frames.Single().SequenceEqual(SysexProtocol.IdentityQuery()), "Exact read delivery");
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        midi.Received += bytes => received.TrySetResult(bytes);
        byte[] fragment = [240, 0, 1]; input.SysEx(fragment); fragment[0] = 0;
        Need((await received.Task.WaitAsync(TimeSpan.FromSeconds(2)))[0] == 240, "Callback bytes copied before returning");
        var shortMessage = new TaskCompletionSource<MidiShortPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        midi.ShortReceived += value => shortMessage.TrySetResult(value);
        input.Short(0x643cb0);
        Need((await shortMessage.Task.WaitAsync(TimeSpan.FromSeconds(2))).Message == 0x643cb0, "Controller events retained");
        var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        midi.Faulted += error => fault.TrySetResult(error);
        input.Error("synthetic disconnect"); await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Reject<IOException>(() => midi.SendReadAsync(SysexProtocol.IdentityQuery(), default));
        await midi.DisposeAsync(); await midi.DisposeAsync();
        Need(released && input.Disposed && output.Disposed, "Ownership released after disposal");

        await using (var onlyInput = new SharedMidiTransport(new Input()))
            await Reject<InvalidOperationException>(() => onlyInput.SendReadAsync(SysexProtocol.IdentityQuery(), default));
        var joiningInput = new Input(); var joiningOutput = new Output();
        await using (var joining = new SharedMidiTransport(joiningInput, joiningOutput))
        {
            var nextReply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            joining.Received += bytes => nextReply.TrySetResult(bytes);
            joiningInput.Error("Discarded invalid MIDI packet; listening continues. Received an out-of-sequence SysEx7 UMP packet.");
            await joining.SendReadAsync(SysexProtocol.IdentityQuery(), default);
            byte[] reply = [0xf0, 0, 1, 0x74, 0x12, 0x64, 0, 0, 0, 0xf7];
            joiningInput.SysEx(reply);
            Need((await nextReply.Task.WaitAsync(TimeSpan.FromSeconds(2))).SequenceEqual(reply) && joiningOutput.Frames.Count == 1,
                "Joining a partial shared stream must still permit the next complete query/reply without reconnect or replay");
        }
        foreach (var definition in new[] { d, DeviceDefinition.Create("FM3"), DeviceDefinition.Create("Axe-Fx III", 512), DeviceDefinition.Create("Axe-Fx III", 1024) })
        {
            var scopedOutput = new Output();
            await using var scoped = new SharedMidiTransport(new Input(), scopedOutput, new PresetSaveScope(definition, 2));
            await Reject<InvalidOperationException>(() => scoped.SendEditorAsync(EditorCommand.SavePreset(definition, 3), default));
            await scoped.SendEditorAsync(EditorCommand.SavePreset(definition, 2), default);
            Need(scopedOutput.Frames.Count == 1, "Exact scope preserved across models");
        }

        // A blocked native send may complete after cancellation; never replay or free it early.
        var blocking = new Output { Block = true }; bool leaseReleased = false;
        var blocked = new SharedMidiTransport(new Input(), blocking, releaseOwnership: () => leaseReleased = true);
        using var cancel = new CancellationTokenSource();
        var send = blocked.SendReadAsync(SysexProtocol.IdentityQuery(), cancel.Token);
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
        await Reject<OperationCanceledException>(() => send);
        await Reject<IOException>(() => blocked.SendReadAsync(SysexProtocol.IdentityQuery(), default));
        var disposing = blocked.DisposeAsync().AsTask();
        Need(!blocking.Disposed && !leaseReleased, "Pending native output retains lifetime and ownership");
        blocking.Resume.Set(); await disposing;
        Need(leaseReleased && blocking.Frames.Count == 1, "Cancelled send not replayed; eventual cleanup");

        var timeoutOutput = new Output { Block = true };
        var timed = new SharedMidiTransport(new Input(), timeoutOutput, sendTimeout: TimeSpan.FromMilliseconds(50));
        await Reject<TimeoutException>(() => timed.SendReadAsync(SysexProtocol.IdentityQuery(), default));
        timeoutOutput.Resume.Set();
        await Reject<IOException>(() => timed.SendReadAsync(SysexProtocol.IdentityQuery(), default));
        await timed.DisposeAsync();
        Need(timeoutOutput.Frames.Count == 1, "Timeout permanently faults transport");

        var overflowInput = new Input(); await using var overflowing = new SharedMidiTransport(overflowInput, new Output());
        using var resumeReceive = new ManualResetEventSlim();
        var enteredReceive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var overflowFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        overflowing.Received += _ => { enteredReceive.TrySetResult(); resumeReceive.Wait(); };
        overflowing.Faulted += error => overflowFault.TrySetResult(error);
        overflowInput.SysEx([240]); await enteredReceive.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try { for (int i = 0; i < 257; i++) overflowInput.SysEx([1]); }
        finally { resumeReceive.Set(); }
        Need((await overflowFault.Task.WaitAsync(TimeSpan.FromSeconds(2))).Message.Contains("overflow"), "Overflow is terminal, never silent loss");
    }
    sealed class Input : IMidiInputPort
    {
        public event EventHandler<MidiShortMessageEventArgs>? MessageReceived;
        public event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived;
        public event EventHandler<string>? ErrorReceived;
        public bool Disposed;
        public void Start() { }
        public void Stop() { }
        public void Dispose() => Disposed = true;
        public void SysEx(byte[] bytes) => SysexMessageReceived?.Invoke(this, new(bytes));
        public void Short(int message) => MessageReceived?.Invoke(this, new(message));
        public void Error(string message) => ErrorReceived?.Invoke(this, message);
    }
    sealed class Output : IMidiOutputPort
    {
        public readonly List<byte[]> Frames = [];
        public bool Disposed, Block;
        public readonly ManualResetEventSlim Resume = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Send(int message) => SendBuffer(Midi1ShortMessageCodec.Decode(message));
        public void SendBuffer(byte[] frame)
        {
            Entered.TrySetResult(); if (Block) Resume.Wait();
            Need(!Disposed, "Output disposed during send"); Frames.Add(frame.ToArray());
        }
        public void Dispose() => Disposed = true;
    }
}
