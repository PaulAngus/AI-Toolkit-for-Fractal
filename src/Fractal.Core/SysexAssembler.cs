// Adapted from PresetNameSync.Core. Added bounded failure reporting and explicit reset.
namespace Fractal.Core;
public sealed class SysexAssembler
{
    private readonly List<byte> buffer = [];
    public bool Incomplete => buffer.Count > 0;
    public void Reset() => buffer.Clear();
    public IEnumerable<byte[]> Feed(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            if (b >= 0xf8) continue;
            if (b == 0xf0) { buffer.Clear(); buffer.Add(b); continue; }
            if (buffer.Count == 0) continue;
            if (b == 0xf7) { buffer.Add(b); byte[] frame = buffer.ToArray(); buffer.Clear(); yield return frame; continue; }
            if (b >= 0x80) { buffer.Clear(); continue; }
            if (buffer.Count >= 4096) { buffer.Clear(); throw new InvalidDataException("SysEx frame exceeds receive bound."); }
            buffer.Add(b);
        }
    }
}
