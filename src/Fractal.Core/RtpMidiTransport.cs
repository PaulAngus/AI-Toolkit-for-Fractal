using System.Net;
using System.Security.Cryptography;
using System.Text;
using MightBeUseful.Midi.Rtp;
namespace Fractal.Core;

/// <summary>Toolkit authorization and endpoint ownership around the shared non-reconnecting initiator.</summary>
public sealed class RtpMidiTransport : IPresetRestoreTransport, IMidiInputSource
{
    public event Action<byte[]>? Received;
    public event Action<Exception>? Faulted;
    public event Action<MidiShortPacket>? ShortReceived;
    private readonly RtpMidiInitiator transport;
    private readonly FileStream lease;
    private readonly MidiSendAuthority authority;
    private RtpMidiTransport(RtpMidiInitiator transport, FileStream lease, EditorScope? scope)
    {
        this.transport = transport; this.lease = lease; authority = new(scope);
        transport.Received += bytes => Received?.Invoke(bytes);
        transport.Faulted += error => Faulted?.Invoke(error);
        transport.ShortReceived += (packed, timestamp) => ShortReceived?.Invoke(new(packed, timestamp));
    }
    public static IPEndPoint ParseEndpoint(string endpoint) => RtpMidiInitiator.ParseEndpoint(endpoint);
    public static async Task<RtpMidiTransport> ConnectAsync(string endpoint, EditorScope? scope = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = ParseEndpoint(endpoint);
        string folder = Path.Combine(Path.GetTempPath(), "fractal-toolkit-network-locks"); Directory.CreateDirectory(folder);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(address.ToString())));
        var lease = new FileStream(Path.Combine(folder, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { return new(await RtpMidiInitiator.ConnectAsync(endpoint, cancellationToken, "Fractal"), lease, scope); }
        catch { lease.Dispose(); throw; }
    }
    public Task SendReadAsync(byte[] request, CancellationToken ct) { authority.Read(request); return transport.SendAsync(request, ct); }
    public Task SendEditorAsync(EditorCommand command, CancellationToken ct) { authority.Editor(command); return transport.SendAsync(command.Frame, ct); }
    public Task SendPresetFrameAsync(PresetUploadPlan plan, int index, CancellationToken ct) { authority.Upload(plan, index); return transport.SendAsync(plan.Frame(index), ct); }
    public async ValueTask DisposeAsync()
    {
        try { await transport.DisposeAsync(); } finally { lease.Dispose(); }
    }
}
