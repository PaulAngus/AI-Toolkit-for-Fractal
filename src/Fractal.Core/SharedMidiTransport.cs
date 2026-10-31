using System.Diagnostics;
using System.Threading.Channels;
using MightBeUseful.Midi;

namespace Fractal.Core;

/// <summary>Toolkit authorization and bounded delivery over shared package ports. Owns supplied ports.</summary>
public class SharedMidiTransport : IPresetRestoreTransport, IMidiInputSource
{
    public string TimestampSource => "host-monotonic-milliseconds-since-transport-open";
    public event Action<byte[]>? Received;
    public event Action<Exception>? Faulted;
    public event Action<MidiShortPacket>? ShortReceived;
    private readonly IMidiInputPort input;
    private readonly IMidiOutputPort? output;
    private readonly MidiSendAuthority authority;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly Channel<(byte[]? Bytes, MidiShortPacket? Short)> received = Channel.CreateBounded<(byte[]?, MidiShortPacket?)>(256);
    private readonly CancellationTokenSource shutdown = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly Task worker;
    private readonly Action? releaseOwnership;
    private Exception? fault;
    private int closing;
    private Task? disposal;
    private readonly object disposeGate = new();
    private readonly TimeSpan sendTimeout;

    public SharedMidiTransport(IMidiInputPort input, IMidiOutputPort? output = null, EditorScope? scope = null,
        Action? releaseOwnership = null, TimeSpan? sendTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (sendTimeout is TimeSpan bound && (bound <= TimeSpan.Zero || bound > TimeSpan.FromSeconds(5)))
            throw new ArgumentOutOfRangeException(nameof(sendTimeout), "Send deadline must be positive and at most five seconds.");
        this.input = input; this.output = output; authority = new(scope);
        this.releaseOwnership = releaseOwnership;
        this.sendTimeout = sendTimeout ?? TimeSpan.FromSeconds(5);
        input.MessageReceived += OnShort;
        input.SysexMessageReceived += OnSysEx;
        input.ErrorReceived += OnError;
        worker = Task.Run(ReceiveLoop);
        try { input.Start(); }
        catch { DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
    }
    private void OnShort(object? sender, MidiShortMessageEventArgs message)
    {
        if ((message.RawMessage & 0xf0) is not (0x80 or 0x90 or 0xb0)) return;
        // Shared 0.3.4 ports do not expose driver timestamps: monotonic receipt time.
        Enqueue((null, new(unchecked((uint)message.RawMessage), unchecked((uint)elapsed.ElapsedMilliseconds))));
    }
    private void OnSysEx(object? sender, MidiSysExMessageEventArgs message)
    {
        if (message.SysexBytes.Length > 65536) { Fail(new IOException("MIDI receive chunk exceeds bound.")); return; }
        Enqueue((message.SysexBytes.ToArray(), null));
    }
    private void OnError(object? sender, string message)
    {
        // The shared input pump has already discarded a partial message when a
        // client joins mid-SysEx and continues listening. It is not a port fault.
        if (message.StartsWith("Discarded invalid MIDI packet; listening continues.", StringComparison.Ordinal))
        { Trace.TraceWarning(message); return; }
        Fail(new IOException(message));
    }
    private void Enqueue((byte[]? Bytes, MidiShortPacket? Short) item)
    {
        if (Volatile.Read(ref closing) != 0 || Volatile.Read(ref fault) != null) return;
        if (!received.Writer.TryWrite(item)) Fail(new IOException("MIDI receive queue overflow."));
    }
    private void Fail(Exception error)
    {
        if (Interlocked.CompareExchange(ref fault, error, null) == null) received.Writer.TryComplete(error);
    }
    private async Task ReceiveLoop()
    {
        try
        {
            await foreach (var item in received.Reader.ReadAllAsync(shutdown.Token))
            {
                if (Volatile.Read(ref fault) is Exception error) throw error;
                if (item.Bytes != null) Received?.Invoke(item.Bytes);
                else if (item.Short != null) ShortReceived?.Invoke(item.Short);
            }
        }
        catch (OperationCanceledException) when (Volatile.Read(ref closing) != 0) { }
        catch (Exception error)
        {
            Fail(error);
            try { Faulted?.Invoke(Volatile.Read(ref fault)!); }
            catch { /* Subscribers cannot unwind into native package callbacks. */ }
        }
    }
    public Task SendReadAsync(byte[] request, CancellationToken token)
    {
        var frame = request.ToArray();
        return SendAsync(frame, false, () => authority.Read(frame), token);
    }
    public Task SendEditorAsync(EditorCommand command, CancellationToken token) => SendAsync(command.Frame, command.IsShortMessage, () => authority.Editor(command), token);
    public Task SendPresetFrameAsync(PresetUploadPlan plan, int index, CancellationToken token) => SendAsync(plan.Frame(index), false, () => authority.Upload(plan, index), token);

    private async Task SendAsync(byte[] bytes, bool shortMessage, Action authorize, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref closing) != 0, this);
        if (Volatile.Read(ref fault) is Exception priorFailure)
            throw new IOException("MIDI session has failed; reconnect deliberately.", priorFailure);
        if (!await sendGate.WaitAsync(sendTimeout, token))
        {
            var busy = new TimeoutException("MIDI send queue deadline exceeded; no command replay.");
            Fail(busy); throw busy;
        }
        Task? sending = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref closing) != 0, this);
            if (Volatile.Read(ref fault) is Exception error) throw new IOException("MIDI session has failed; reconnect deliberately.", error);
            if (output == null) throw new InvalidOperationException("Input-only connection cannot send MIDI.");
            token.ThrowIfCancellationRequested();
            authorize();
            // The shared synchronous send cannot be interrupted safely. On timeout/cancellation
            // poison this session and retain ownership until the native call and disposal finish.
            sending = Task.Run(() =>
            {
                if (shortMessage) output.Send(Midi1ShortMessageCodec.Encode(bytes));
                else output.SendBuffer(bytes);
            }, CancellationToken.None);
            try { await sending.WaitAsync(sendTimeout, token); }
            catch (Exception sendError) { Fail(sendError); throw; }
        }
        finally
        {
            if (sending is { IsCompleted: false })
                _ = sending.ContinueWith(t => { _ = t.Exception; sendGate.Release(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else sendGate.Release();
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (disposeGate) return new(disposal ??= DisposeCore());
    }
    private async Task DisposeCore()
    {
        Interlocked.Exchange(ref closing, 1);
        input.MessageReceived -= OnShort; input.SysexMessageReceived -= OnSysEx; input.ErrorReceived -= OnError;
        shutdown.Cancel(); received.Writer.TryComplete();
        // Cleanup may outlive the caller's bounded wait if a driver is hung. Do not release
        // ownership or dispose an output while a synchronous native send still uses it.
        string cleanupPhase = "waiting for the MIDI send to finish";
        var cleanup = Task.Run(async () =>
        {
            await sendGate.WaitAsync();
            try
            {
                Volatile.Write(ref cleanupPhase, "waiting for the receive worker to stop");
                await worker;
                Exception? failure = null;
                Volatile.Write(ref cleanupPhase, "stopping the MIDI input");
                try { input.Stop(); } catch (Exception error) { failure = error; }
                Volatile.Write(ref cleanupPhase, "closing the MIDI input");
                try { input.Dispose(); } catch (Exception error) { failure ??= error; }
                Volatile.Write(ref cleanupPhase, "closing the MIDI output");
                try { output?.Dispose(); } catch (Exception error) { failure ??= error; }
                if (failure != null) throw new IOException("MIDI cleanup failed; ownership retained.", failure);
                Volatile.Write(ref cleanupPhase, "releasing MIDI port ownership");
                releaseOwnership?.Invoke();
            }
            finally { sendGate.Release(); }
        });
        _ = cleanup.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        try { await cleanup.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException error)
        { throw new TimeoutException("MIDI cleanup timed out while " + Volatile.Read(ref cleanupPhase) + "; ownership retained.", error); }
    }
}
