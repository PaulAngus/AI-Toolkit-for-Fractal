using MightBeUseful.Midi;
#if WINDOWS
using MightBeUseful.Midi.Windows;
#endif
namespace Fractal.Core;

public sealed record MidiPort(int Index, string Name);
/// <summary>Numeric Windows MIDI selection using the shared current/legacy backend policy.</summary>
public sealed class WinMmTransport : SharedMidiTransport
{
    private sealed record PortsLease(IMidiInputPort Input, IMidiOutputPort? Output, Action Release);
    private WinMmTransport(PortsLease ports, EditorScope? scope) : base(ports.Input, ports.Output, scope, ports.Release) { }
    public WinMmTransport(int inputIndex, int outputIndex, EditorScope? pilotScope = null) : this(Open(inputIndex, outputIndex), pilotScope) { }
    public static WinMmTransport OpenInput(int inputIndex) => new(Open(inputIndex, null), null);
    public static MidiPort[] Ports(bool inputs)
    {
#if WINDOWS
        if (WindowsMidiBackend.Capabilities.UseMidiServices)
        {
            var currentNames = inputs ? WindowsMidiBackend.GetInputDevices().Select(p => p.Name)
                : WindowsMidiBackend.GetOutputDevices().Select(p => p.Name);
            return currentNames.Select((name, index) => new MidiPort(index, name)).ToArray();
        }
        var names = inputs ? WinMmPortCatalog.GetInputNames() : WinMmPortCatalog.GetOutputNames();
        return names.Select((name, index) => new MidiPort(index, name)).ToArray();
#else
        throw new PlatformNotSupportedException("Native MIDI requires a Windows build. Offline and RTP-MIDI operations are portable.");
#endif
    }
    private static PortsLease Open(int inputIndex, int? outputIndex)
    {
#if WINDOWS
        if (WindowsMidiBackend.Capabilities.UseMidiServices) return OpenCurrent(inputIndex, outputIndex);
        var inputs = Ports(true);
        if (inputIndex < 0 || inputIndex >= inputs.Length || outputIndex is int i && (i < 0 || i >= Ports(false).Length))
            throw new ArgumentOutOfRangeException(nameof(inputIndex), "MIDI port index is unavailable.");
        string inputId = ResolveInputId(inputs, inputIndex, WindowsLegacyMidiBackend.GetInputDevices());
        var locks = new List<Semaphore>();
        void Release() { foreach (var item in locks) { item.Release(); item.Dispose(); } }
        void Lock(string direction, int index)
        {
            var item = new Semaphore(1, 1, $"Local\\FractalAIToolkit-{direction}-{index}");
            if (!item.WaitOne(0)) { item.Dispose(); throw new IOException("Another toolkit session owns this MIDI port."); }
            locks.Add(item);
        }
        IMidiInputPort? input = null; IMidiOutputPort? output = null;
        try
        {
            Lock("input", inputIndex);
            if (outputIndex is int selected) { Lock("output", selected); output = WindowsLegacyMidiBackend.OpenOutput(selected); }
            input = WindowsLegacyMidiBackend.OpenInput(inputId);
            return new(input, output, Release);
        }
        catch
        {
            // A failed close must retain the ownership lock.
            input?.Dispose(); output?.Dispose(); Release(); throw;
        }
#else
        throw new PlatformNotSupportedException("Native MIDI requires a Windows build. Offline and RTP-MIDI operations are portable.");
#endif
    }
#if WINDOWS
    private static PortsLease OpenCurrent(int inputIndex, int? outputIndex)
    {
        var inputs = WindowsMidiBackend.GetInputDevices();
        var outputs = outputIndex.HasValue ? WindowsMidiBackend.GetOutputDevices() : [];
        if (inputIndex < 0 || inputIndex >= inputs.Count || outputIndex is int selected && (selected < 0 || selected >= outputs.Count))
            throw new ArgumentOutOfRangeException(nameof(inputIndex), "MIDI port index is unavailable.");
        var locks = new List<Semaphore>();
        void Release() { foreach (var item in locks) { item.Release(); item.Dispose(); } }
        void Lock(string direction, string identity)
        {
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity.ToUpperInvariant())));
            var item = new Semaphore(1, 1, "Local\\FractalAIToolkit-" + direction + "-" + key);
            if (!item.WaitOne(0)) { item.Dispose(); throw new IOException("Another toolkit session owns this MIDI port."); }
            locks.Add(item);
        }
        IMidiInputPort? input = null; IMidiOutputPort? output = null;
        try
        {
            // Names must remain unambiguous for the public numeric selection.
            if (inputs.Count(p => p.Name == inputs[inputIndex].Name) != 1 ||
                outputIndex is int check && outputs.Count(p => p.Name == outputs[check].Name) != 1)
                throw new IOException("Select an unambiguous MIDI endpoint.");
            Lock("input", inputs[inputIndex].Id);
            if (outputIndex is int target)
            {
                Lock("output", outputs[target].Name);
                output = WindowsMidiBackend.OpenOutput(outputs[target].Index);
            }
            input = WindowsMidiBackend.OpenInput(inputs[inputIndex].Id);
            return new(input, output, Release);
        }
        catch { input?.Dispose(); output?.Dispose(); Release(); throw; }
    }
#endif
    // WinRT may append a per-device numeric suffix to a WinMM display name.
    // Accept only one exact or indexed alias, with no competing WinMM endpoint.
    internal static string ResolveInputId(IReadOnlyList<MidiPort> catalog, int inputIndex,
        IReadOnlyList<(string Id, string Name)> shared)
    {
        var selected = catalog.Where(p => p.Index == inputIndex).ToArray();
        if (selected.Length != 1) throw new ArgumentOutOfRangeException(nameof(inputIndex));
        string name = selected[0].Name;
        bool Matches(string candidate)
        {
            if (candidate == name) return true;
            string prefix = name + " [";
            if (!candidate.StartsWith(prefix, StringComparison.Ordinal) || !candidate.EndsWith(']')) return false;
            var suffix = candidate.AsSpan(prefix.Length, candidate.Length - prefix.Length - 1);
            return uint.TryParse(suffix, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        }
        var candidates = shared.Where(p => Matches(p.Name)).ToArray();
        if (catalog.Count(p => p.Name == name) != 1 || candidates.Length != 1 ||
            catalog.Any(p => p.Index != inputIndex && p.Name == candidates[0].Name))
            throw new IOException("Cannot uniquely map the selected WinMM input to the shared MIDI backend. Select an unambiguous endpoint.");
        return candidates[0].Id;
    }
}
