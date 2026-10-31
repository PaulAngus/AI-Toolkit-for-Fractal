namespace Fractal.Core;

/// <summary>Command-line process connection selection; library callers use the transports directly.</summary>
public static class TransportConnection
{
    private static string? endpoint;
    public static string? NetworkEndpoint => endpoint;
    public static void Configure(ref string[] arguments, bool hardwareHelper = false)
    {
        int index = Array.IndexOf(arguments, "--network");
        if (index < 0) return;
        if (endpoint != null || index + 1 >= arguments.Length || arguments.Count(a => a == "--network") != 1 || arguments.Contains("--input") || arguments.Contains("--output"))
            throw new ArgumentException("Choose --network IPv4:CONTROL_PORT or explicit local ports, without mixing or repetition.");
        string command = arguments.FirstOrDefault() ?? "";
        if (!hardwareHelper && command is not ("inspect" or "snapshot" or "inventory" or "scene-midi" or "prepare-load" or "read-block-parameters" or "amp-roster" or "capture-control" or "capture-performance" or "live-monitor" or "listen-notifications" or "listen-midi"))
            throw new ArgumentException("Network selection applies only to device operations.");
        endpoint = RtpMidiTransport.ParseEndpoint(arguments[index + 1]).ToString();
        var remaining = arguments.Where((_, i) => i != index && i != index + 1).ToList();
        // Existing task parsers consume port placeholders; OpenAsync never opens native ports in network mode.
        remaining.AddRange(command is "listen-notifications" or "listen-midi" ? ["--input", "0"] : ["--input", "0", "--output", "0"]);
        arguments = remaining.ToArray();
    }
#if FRACTAL_NO_MIDI
    // Development pipeline variant: validation commands cannot open local or network MIDI,
    // even if an earlier argument-validation regression accidentally reaches this boundary.
    public static Task<IPresetRestoreTransport> OpenAsync(int input, int output, EditorScope? scope = null, CancellationToken cancellationToken = default) =>
        Task.FromException<IPresetRestoreTransport>(new InvalidOperationException("MIDI connections are disabled in this offline pipeline build."));
    public static Task<IPresetRestoreTransport> OpenInputAsync(int input, CancellationToken cancellationToken = default) =>
        Task.FromException<IPresetRestoreTransport>(new InvalidOperationException("MIDI connections are disabled in this offline pipeline build."));
#else
    public static async Task<IPresetRestoreTransport> OpenAsync(int input, int output, EditorScope? scope = null, CancellationToken cancellationToken = default) =>
        endpoint is string network ? await RtpMidiTransport.ConnectAsync(network, scope, cancellationToken) : NativeMidiTransport.Open(input, output, scope, cancellationToken);
    public static async Task<IPresetRestoreTransport> OpenInputAsync(int input, CancellationToken cancellationToken = default) =>
        endpoint is string network ? await RtpMidiTransport.ConnectAsync(network, cancellationToken: cancellationToken) : NativeMidiTransport.OpenInput(input, cancellationToken);
#endif
}
