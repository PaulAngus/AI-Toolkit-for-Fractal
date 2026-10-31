namespace Fractal.Core;

/// <summary>Local native selection only. RTP selection remains in TransportConnection.</summary>
public static class NativeMidiTransport
{
    public static MidiPort[] Ports(bool inputs) => OperatingSystem.IsMacOS() ? CoreMidiTransport.Ports(inputs)
        : OperatingSystem.IsWindows() ? WinMmTransport.Ports(inputs) : throw Unsupported();
    public static SharedMidiTransport Open(int input, int output, EditorScope? scope = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return OperatingSystem.IsMacOS() ? new CoreMidiTransport(input, output, scope)
            : OperatingSystem.IsWindows() ? new WinMmTransport(input, output, scope) : throw Unsupported();
    }
    public static SharedMidiTransport OpenInput(int input, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return OperatingSystem.IsMacOS() ? CoreMidiTransport.OpenInput(input)
            : OperatingSystem.IsWindows() ? WinMmTransport.OpenInput(input) : throw Unsupported();
    }
    private static PlatformNotSupportedException Unsupported() => new("Native MIDI supports Windows and macOS. Offline and RTP-MIDI operations remain portable.");
}
