using System.Buffers.Binary;

namespace Fractal.Core;

/// <summary>Inverse of the contained bounded Huffman/container reader. Preserves opaque framing/header/padding.</summary>
public static class PresetBodyWriter
{
    private sealed record Node(int Symbol, Node? Left = null, Node? Right = null);
    public static byte[] ReplaceBody(DeviceDefinition device, byte[] baseline, byte[] body)
    {
        var original = SnapshotExport.DecodeFile(device, baseline);
        if (body.Length is < 260 or > 65535) throw new InvalidDataException("Authored body exceeds the saved format bounds.");
        if (body.SequenceEqual(original.Image.Body)) return baseline.ToArray();
        var counts = new int[256]; foreach (byte value in body) counts[value]++;
        var queue = new PriorityQueue<Node, (int Weight, int Order)>(); int order = 0;
        for (int symbol = 0; symbol < 256; symbol++) if (counts[symbol] > 0) queue.Enqueue(new(symbol), (counts[symbol], order++));
        while (queue.Count > 1)
        {
            queue.TryDequeue(out var left, out var lw); queue.TryDequeue(out var right, out var rw);
            queue.Enqueue(new(-1, left, right), (lw.Weight + rw.Weight, order++));
        }
        var root = queue.Dequeue(); var bits = new List<bool>(); var codes = new bool[256][];
        void Tree(Node node, bool[] prefix)
        {
            bits.Add(node.Symbol >= 0);
            if (node.Symbol >= 0)
            {
                for (int bit = 7; bit >= 0; bit--) bits.Add((node.Symbol & (1 << bit)) != 0);
                codes[node.Symbol] = prefix;
            }
            else { Tree(node.Left!, [.. prefix, false]); Tree(node.Right!, [.. prefix, true]); }
        }
        Tree(root, []); foreach (byte symbol in body) bits.AddRange(codes[symbol]);
        byte[] compressed = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) compressed[i / 8] |= (byte)(1 << (7 - i % 8));
        byte[] raw = original.Image.RawImage.ToArray();
        if (compressed.Length > raw.Length - 0x4c) throw new InvalidDataException("Authored preset exceeds this model's image capacity.");
        int originalCompressedLength = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0x4a));
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x48), (ushort)body.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x4a), (ushort)compressed.Length);
        compressed.CopyTo(raw, 0x4c);
        // The old stream may extend beyond its replacement. Those bytes are no longer
        // compressed content; clear them while retaining padding past both streams.
        if (compressed.Length < originalCompressedLength)
            raw.AsSpan(0x4c + compressed.Length, originalCompressedLength - compressed.Length).Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4), StoredPresetDecoder.ComputeCrc(raw));
        byte[] result = PackRaw(device, baseline, raw);
        var decoded = SnapshotExport.DecodeFile(device, result);
        if (!decoded.Image.Body.SequenceEqual(body)) throw new InvalidDataException("Authored image failed independent decompression.");
        EditorPreservation.VerifyRawRepresentation(original.Image, decoded.Image);
        return result;
    }
    public static byte[] RenamePreset(DeviceDefinition device, byte[] baseline, string name)
    {
        if (name == null || name.Length > 31 || name.Any(c => c is < ' ' or > '~') || name.EndsWith(' '))
            throw new ArgumentException("Preset name must be 0–31 printable ASCII characters without trailing spaces.");
        var original = SnapshotExport.DecodeFile(device, baseline);
        if (name == original.Image.Name) return baseline.ToArray();
        byte[] raw = original.Image.RawImage.ToArray(); raw.AsSpan(8, 32).Clear();
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(raw, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4), StoredPresetDecoder.ComputeCrc(raw));
        byte[] result = PackRaw(device, baseline, raw);
        var changed = SnapshotExport.DecodeFile(device, result).Image;
        if (changed.Name != name || !changed.Body.SequenceEqual(original.Image.Body) ||
            Enumerable.Range(0, raw.Length).Any(i => i is not (4 or 5) && i is not (>= 8 and < 40) && raw[i] != original.Image.RawImage[i]))
            throw new InvalidDataException("Preset rename changed unrelated bytes.");
        return result;
    }
    private static byte[] PackRaw(DeviceDefinition device, byte[] baseline, byte[] raw)
    {
        byte[][] frames = new SysexAssembler().Feed(baseline).ToArray(); int xor = 0;
        for (int chunk = 0; chunk < device.DumpChunks; chunk++)
        {
            var frame = frames[chunk + 1];
            for (int word = 0; word < 1024; word++)
            {
                int value = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(chunk * 2048 + word * 2)); xor ^= value;
                frame[8 + word * 3] = (byte)(value & 127); frame[9 + word * 3] = (byte)((value >> 7) & 127); frame[10 + word * 3] = (byte)(value >> 14);
            }
            frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
        }
        frames[^1] = SysexProtocol.Frame(device.ModelByte, 0x79, (byte)(xor & 127), (byte)((xor >> 7) & 127), (byte)(xor >> 14));
        return frames.SelectMany(f => f).ToArray();
    }
}
