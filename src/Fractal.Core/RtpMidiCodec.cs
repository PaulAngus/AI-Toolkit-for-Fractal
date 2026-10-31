namespace Fractal.Core;

public sealed record RtpMidiMessage(uint Timestamp, byte[] Bytes);

/// <summary>Strict shared RTP codec; no loss reconstruction or replay.</summary>
public sealed class RtpMidiCodec(uint source)
{
    private readonly MightBeUseful.Midi.Rtp.RtpMidiCodec codec = new(source);
    public bool Incomplete => codec.Incomplete;
    public IReadOnlyList<RtpMidiMessage> Decode(byte[] packet) =>
        codec.Decode(packet).Select(m => new RtpMidiMessage(m.Timestamp, m.Bytes)).ToArray();
    public static byte[][] Encode(byte[] message, uint source, ref ushort sequence, uint timestamp) =>
        MightBeUseful.Midi.Rtp.RtpMidiCodec.Encode(message, source, ref sequence, timestamp);
}
