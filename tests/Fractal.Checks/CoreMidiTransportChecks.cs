using System.Diagnostics;
using Fractal.Core;
using MightBeUseful.Midi;

static class CoreMidiTransportChecks
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    public static async Task Run()
    {
        var backend = new Backend();
        Need(CoreMidiTransport.Catalog(backend, true).Select(p => p.Index).SequenceEqual(new[] { 2, 7 }), "Sparse indices must not be renumbered");
        Need(backend.Opens == 0, "Listing does not open fake ports");
        var lease = new Lease();
        Reject<ArgumentOutOfRangeException>(() => CoreMidiTransport.OpenForChecks(backend, 0, 5, null, (_, _) => lease));
        Need(backend.Opens == 0 && !lease.Released, "Invalid selection refused before lease/open");
        backend.BadIds = true;
        Reject<IOException>(() => CoreMidiTransport.Catalog(backend, true));
        backend.BadIds = false;
        await using (var inputOnly = CoreMidiTransport.OpenForChecks(backend, 7, null, null, (_, o) => { Need(o == null, "Input-only ownership"); return lease; }))
        {
            Need(backend.InputId == "7" && backend.OutputIndex == null && backend.OutputLists == 0, "Input-only does not enumerate/open output");
            await RejectAsync<InvalidOperationException>(() => inputOnly.SendReadAsync(SysexProtocol.IdentityQuery(), default));
        }
        Need(lease.Released && backend.Input.Disposed, "Input-only cleanup releases ownership");
        foreach (var d in new[] { DeviceDefinition.Create("FM9"), DeviceDefinition.Create("FM3"), DeviceDefinition.Create("Axe-Fx III", 512), DeviceDefinition.Create("Axe-Fx III", 1024) })
        {
            backend = new Backend(); lease = new Lease();
            await using (var scoped = CoreMidiTransport.OpenForChecks(backend, 2, 5, new PresetSaveScope(d, 2), (_, _) => lease))
            {
                Need(backend.OutputIndex == 5 && backend.InputId == "2", "Exact package indices selected");
                await RejectAsync<InvalidOperationException>(() => scoped.SendReadAsync(EditorCommand.SavePreset(d, 2).Frame, default));
                await RejectAsync<InvalidOperationException>(() => scoped.SendEditorAsync(EditorCommand.SavePreset(d, 3), default));
                Need(backend.Output.Sends == 0, "Unauthorized frames never reach Mac backend");
                await scoped.SendReadAsync(SysexProtocol.IdentityQuery(), default);
                await scoped.SendEditorAsync(EditorCommand.SavePreset(d, 2), default);
                Need(backend.Output.Sends == 2, "Authorized operations use common guarded adapter");
            }
            Need(lease.Released && backend.Input.Disposed && backend.Output.Disposed, "Both ports closed before lease release");
        }
        backend = new Backend { FailInputOpen = true }; lease = new Lease();
        Reject<IOException>(() => CoreMidiTransport.OpenForChecks(backend, 2, 5, null, (_, _) => lease));
        Need(lease.Released && backend.Output.Disposed, "Partial open closes output and releases lease");
        backend = new Backend(); backend.Input.FailStart = true; lease = new Lease();
        Reject<IOException>(() => CoreMidiTransport.OpenForChecks(backend, 2, 5, null, (_, _) => lease));
        Need(lease.Released && backend.Input.Disposed && backend.Output.Disposed, "Start failure closes ports");
        backend = new Backend { FailInputOpen = true }; backend.Output.FailDispose = true; lease = new Lease();
        Reject<IOException>(() => CoreMidiTransport.OpenForChecks(backend, 2, 5, null, (_, _) => lease));
        Need(!lease.Released, "Failed native cleanup retains ownership");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => NativeMidiTransport.Open(2, 5, token: cancelled.Token));
        Reject<OperationCanceledException>(() => NativeMidiTransport.OpenInput(2, cancelled.Token));
        // Actual file leases, including a separate process, but no MIDI enumeration or native calls.
        string directory = Path.Combine(Path.GetTempPath(), "fractal-coremidi-lease-check-" + Guid.NewGuid().ToString("N"));
        using (var held = NativeMidiPortLease.Acquire(2, 5, directory))
        {
            Reject<IOException>(() => NativeMidiPortLease.Acquire(2, null, directory));
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Cannot locate the check runner."))
                { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(CoreMidiTransportChecks).Assembly.Location);
            start.ArgumentList.Add("--native-lease-probe"); start.ArgumentList.Add(directory);
            using var child = Process.Start(start) ?? throw new Exception("Could not start lease probe");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await child.WaitForExitAsync(deadline.Token); }
            catch { child.Kill(entireProcessTree: true); throw; }
            Need(child.ExitCode == 23, "Second process must refuse an owned port");
            Reject<IOException>(() => NativeMidiPortLease.Acquire(7, 5, directory));
            using var recovered = NativeMidiPortLease.Acquire(7, null, directory); // failed pair must release its input lease
        }
        using (var reopened = NativeMidiPortLease.Acquire(2, 5, directory)) { }
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
    sealed class Lease : IDisposable { public bool Released; public void Dispose() => Released = true; }
    sealed class Backend : ICoreMidiPortBackend
    {
        public bool BadIds, FailInputOpen;
        public int Opens, OutputLists;
        public string? InputId;
        public int? OutputIndex;
        public Input Input = new(); public Output Output = new();
        public IReadOnlyList<(string Id, string Name)> Inputs() => BadIds ? [("2", "Synthetic"), ("2", "Other")] : [("2", "Synthetic"), ("7", "Synthetic")];
        public IReadOnlyList<(int Index, string Name)> Outputs() { OutputLists++; return [(5, "Synthetic")]; }
        public IMidiInputPort OpenInput(string id) { Opens++; InputId = id; if (FailInputOpen) throw new IOException("Synthetic open failure"); return Input; }
        public IMidiOutputPort OpenOutput(int index) { Opens++; OutputIndex = index; return Output; }
    }
    sealed class Input : IMidiInputPort
    {
        public event EventHandler<MidiShortMessageEventArgs>? MessageReceived { add { } remove { } }
        public event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived { add { } remove { } }
        public event EventHandler<string>? ErrorReceived { add { } remove { } }
        public bool Disposed, FailStart;
        public void Start() { if (FailStart) throw new IOException("Synthetic start failure"); }
        public void Stop() { }
        public void Dispose() => Disposed = true;
    }
    sealed class Output : IMidiOutputPort
    {
        public int Sends; public bool Disposed, FailDispose;
        public void Send(int message) => Sends++;
        public void SendBuffer(byte[] message) => Sends++;
        public void Dispose() { Disposed = true; if (FailDispose) throw new IOException("Synthetic close failure"); }
    }
}
