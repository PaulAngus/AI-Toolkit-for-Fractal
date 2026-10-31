using System.Globalization;
using MightBeUseful.Midi;
using MightBeUseful.Midi.CoreMidi;

namespace Fractal.Core;

internal interface ICoreMidiPortBackend
{
    IReadOnlyList<(string Id, string Name)> Inputs();
    IReadOnlyList<(int Index, string Name)> Outputs();
    IMidiInputPort OpenInput(string id);
    IMidiOutputPort OpenOutput(int index);
}

/// <summary>Standard shared CoreMIDI ports; Fractal authorization stays in SharedMidiTransport.</summary>
public sealed class CoreMidiTransport : SharedMidiTransport
{
    private sealed record PortsLease(IMidiInputPort Input, IMidiOutputPort? Output, IDisposable Ownership);
    private CoreMidiTransport(PortsLease ports, EditorScope? scope) : base(ports.Input, ports.Output, scope, ports.Ownership.Dispose) { }
    public CoreMidiTransport(int inputIndex, int outputIndex, EditorScope? scope = null) : this(Open(Backend(), inputIndex, outputIndex), scope) { }
    public static CoreMidiTransport OpenInput(int inputIndex) => new(Open(Backend(), inputIndex, null), null);
    public static MidiPort[] Ports(bool inputs) => Catalog(Backend(), inputs);

    private sealed class StandardBackend : ICoreMidiPortBackend
    {
        public IReadOnlyList<(string Id, string Name)> Inputs() => CoreMidiBackend.GetInputDevices();
        public IReadOnlyList<(int Index, string Name)> Outputs() => CoreMidiBackend.GetOutputDevices();
        public IMidiInputPort OpenInput(string id) => CoreMidiBackend.OpenInput(id);
        public IMidiOutputPort OpenOutput(int index) => CoreMidiBackend.OpenOutput(index);
    }
    private static ICoreMidiPortBackend Backend()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Native CoreMIDI requires macOS.");
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture is not
            (System.Runtime.InteropServices.Architecture.Arm64 or System.Runtime.InteropServices.Architecture.X64))
            throw new PlatformNotSupportedException("Shared CoreMIDI assets support arm64 and x64 processes.");
        return new StandardBackend();
    }
    internal static MidiPort[] Catalog(ICoreMidiPortBackend backend, bool inputs)
    {
        var ports = inputs
            ? backend.Inputs().Select(p => new MidiPort(ParseIndex(p.Id), p.Name)).ToArray()
            : backend.Outputs().Select(p => new MidiPort(p.Index, p.Name)).ToArray();
        if (ports.Any(p => p.Index < 0) || ports.Select(p => p.Index).Distinct().Count() != ports.Length)
            throw new IOException("CoreMIDI catalogue contains invalid or ambiguous indices.");
        return ports;
    }
    private static int ParseIndex(string id) => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= 0
        && id == value.ToString(CultureInfo.InvariantCulture) ? value : throw new IOException("CoreMIDI input has an unsupported index identifier.");
    private static PortsLease Open(ICoreMidiPortBackend backend, int input, int? output,
        Func<int, int?, IDisposable>? acquire = null)
    {
        if (!Catalog(backend, true).Any(p => p.Index == input) ||
            output is int selected && !Catalog(backend, false).Any(p => p.Index == selected))
            throw new ArgumentOutOfRangeException(nameof(input), "MIDI port index is unavailable; enumerate ports again.");
        var ownership = (acquire ?? ((i, o) => NativeMidiPortLease.Acquire(i, o)))(input, output);
        IMidiInputPort? inputPort = null; IMidiOutputPort? outputPort = null;
        try
        {
            if (output is int index) outputPort = backend.OpenOutput(index);
            inputPort = backend.OpenInput(input.ToString(CultureInfo.InvariantCulture));
            return new(inputPort, outputPort, ownership);
        }
        catch
        {
            Exception? cleanupError = null;
            try { inputPort?.Dispose(); } catch (Exception error) { cleanupError = error; }
            try { outputPort?.Dispose(); } catch (Exception error) { cleanupError ??= error; }
            if (cleanupError != null) throw new IOException("CoreMIDI open/cleanup failed; ownership retained.", cleanupError);
            ownership.Dispose(); throw;
        }
    }
    internal static CoreMidiTransport OpenForChecks(ICoreMidiPortBackend backend, int input, int? output,
        EditorScope? scope, Func<int, int?, IDisposable> acquire) => new(Open(backend, input, output, acquire), scope);
}
