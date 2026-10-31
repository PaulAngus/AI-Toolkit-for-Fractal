// Synthetic only. Huffman fixture technique adapted from PresetMaestro.Tests/StoredSceneTests.cs.
using System.Buffers.Binary;
using System.Text;
using Fractal.Core;

static class Fixtures
{
    public static SavedCapture Preset(DeviceDefinition d, int slot, bool midi = false, bool amp = true,
        bool unknownAmpLayout = false, bool withSystemTail = false, int[]? midiFields = null)
    {
        d.ValidateSlot(slot);
        byte[] body = new byte[4000];
        void Word(int off, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(off), (ushort)value);
        Word(0, 17); Word(2, 6); // opaque synthetic header values, no default-scene interpretation
        for (int s = 0; s < 8; s++) Encoding.ASCII.GetBytes($"Synthetic scene {s + 1}").CopyTo(body, 4 + s * 32);
        if (amp) Word(0x104 + (d.GridRows * d.GridColumns - 1) * 4, 58);
        if (midi) Word(0x104, 190);
        int pos = 0x200;
        for (int m = 0; m < 2; m++) { Word(pos + 30, 25); Word(pos + 32, 1); pos += 96; }
        if (amp)
        {
            Word(pos + 30, unknownAmpLayout ? d.AmpColumns + 1 : d.AmpColumns); Word(pos + 32, 4);
            for (int ch = 0; ch < 4; ch++)
            {
                int off = d.AmpTypeWord < 0 ? ch == 0 ? pos + 34 : pos + 46 + ((ch - 1) * d.AmpColumns + d.AmpColumns - 6) * 2 :
                    pos + 46 + (ch * d.AmpColumns + d.AmpTypeWord) * 2;
                Word(off, 20 + ch);
            }
            for (int s = 0; s < 8; s++) { Word(pos + (s - 1) * 2, s % 4); Word(pos + (s + 7) * 2, s % 2); }
            pos += (23 + d.AmpColumns * 4) * 2;
        }
        if (midi)
        {
            Word(pos + 30, 257); Word(pos + 32, 1);
            // Independent synthetic inputs: PC 1/ch 1 in slot 1 across eight
            // scenes, Off slots with deliberately nonzero inactive fields.
            var fields = midiFields ?? DefaultMidiFields();
            if (fields.Length != 256) throw new ArgumentException("Expected 256 synthetic table fields.");
            for (int i = 0; i < fields.Length; i++) Word(pos + 34 + i * 2, fields[i]);
            Word(pos + 46 + 251 * 2, 201); // opaque suffix sentinel, outside the message table
            pos += (23 + 257) * 2;
        }
        if (withSystemTail)
        {
            if (d.OpaqueTailColumns is not int columns || d.OpaqueTailBytes is not int bytes) throw new ArgumentException("No qualified tail definition.");
            Word(pos + 30, columns); Word(pos + 32, 1); Word(pos + 34, 123);
            Array.Resize(ref body, pos + bytes);
        }
        return FromBody(d, slot, body);
    }
    public static SavedCapture FromBody(DeviceDefinition d, int slot, byte[] body, string name = "Synthetic preset")
    {
        var bits = new Bits();
        void Tree(int low, int high)
        {
            if (low == high) { bits.Add(1, 1); bits.Add(low, 8); return; }
            bits.Add(0, 1); int mid = (low + high) / 2; Tree(low, mid); Tree(mid + 1, high);
        }
        Tree(0, 255); foreach (byte b in body) bits.Add(b, 8);
        byte[] compressed = bits.Bytes();
        byte[] raw = new byte[d.DumpChunks * 2048];
        void RawWord(int off, int value) => BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(off), (ushort)value);
        RawWord(2, 0xaa55); Encoding.ASCII.GetBytes(name).CopyTo(raw, 8);
        RawWord(0x48, body.Length); RawWord(0x4a, compressed.Length); compressed.CopyTo(raw, 0x4c);
        RawWord(4, StoredPresetDecoder.ComputeCrc(raw));
        List<byte[]> frames = [SysexProtocol.Frame(d.ModelByte, 0x77, (byte)(slot >> 7), (byte)(slot & 127), 0,
            d.ModelByte == 0x10 ? (byte)0 : (byte)0x40, d.ModelByte == 0x10 ? (byte)1 : (byte)0)];
        int xor = 0;
        for (int chunk = 0; chunk < d.DumpChunks; chunk++)
        {
            // Synthetic content with repeated leading fields, as observed on FM9 12.00.
            // No user preset bytes are included in these fixtures.
            byte[] payload = new byte[3074]; payload[1] = 0x08;
            for (int w = 0; w < 1024; w++)
            {
                ushort word = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(chunk * 2048 + w * 2)); xor ^= word;
                payload[2 + w * 3] = (byte)(word & 127); payload[3 + w * 3] = (byte)((word >> 7) & 127); payload[4 + w * 3] = (byte)(word >> 14);
            }
            frames.Add(SysexProtocol.Frame(d.ModelByte, 0x78, payload));
        }
        frames.Add(SysexProtocol.Frame(d.ModelByte, 0x79, (byte)(xor & 127), (byte)((xor >> 7) & 127), (byte)(xor >> 14)));
        return new(new(slot, name, Enumerable.Range(1, 8).Select(s => $"Synthetic scene {s}").ToArray(), raw, body),
            frames.SelectMany(f => f).ToArray());
    }
    public static byte[][] Frames(byte[] sysex) => new SysexAssembler().Feed(sysex).ToArray();
    public static SavedCapture WithEquivalentCompression(DeviceDefinition d, SavedCapture source)
    {
        byte[] original = source.Image.RawImage;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(0x4a));
        for (int mask = 1; mask <= 128; mask <<= 1)
        {
            byte[] raw = original.ToArray(); raw[0x4c + length - 1] ^= (byte)mask;
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4), StoredPresetDecoder.ComputeCrc(raw));
            byte[][] frames = Frames(source.Sysex); int xor = 0;
            for (int chunk = 0; chunk < d.DumpChunks; chunk++)
            {
                var frame = frames[chunk + 1];
                for (int word = 0; word < 1024; word++)
                {
                    int value = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(chunk * 2048 + word * 2)); xor ^= value;
                    frame[8 + word * 3] = (byte)(value & 127); frame[9 + word * 3] = (byte)((value >> 7) & 127); frame[10 + word * 3] = (byte)(value >> 14);
                }
                frame[^2] = SysexProtocol.ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
            }
            frames[^1] = SysexProtocol.Frame(d.ModelByte, 0x79, (byte)(xor & 127), (byte)((xor >> 7) & 127), (byte)(xor >> 14));
            try
            {
                var candidate = SnapshotExport.DecodeFile(d, frames.SelectMany(f => f).ToArray());
                if (candidate.Image.Body.SequenceEqual(source.Image.Body)) return candidate;
            }
            catch (InvalidDataException) { }
        }
        throw new InvalidDataException("Synthetic preset has no unused compressed padding bit.");
    }
    public static int[] DefaultMidiFields()
    {
        int[] fields = new int[256];
        for (int s = 0; s < 8; s++)
        {
            fields[s * 8] = 2; fields[192 + s * 8] = 1;
            for (int slot = 1; slot < 8; slot++)
            { fields[64 + s * 8 + slot] = 3000; fields[128 + s * 8 + slot] = 12345; fields[192 + s * 8 + slot] = 54321; }
        }
        return fields;
    }
    public static byte[][] LiveTable(DeviceDefinition d, int[] fields)
    {
        byte[] packed = new byte[770]; packed[1] = 2; // opaque metadata
        for (int i = 0; i < 256; i++)
        { packed[2 + i * 3] = (byte)(fields[i] & 127); packed[3 + i * 3] = (byte)((fields[i] >> 7) & 127); packed[4 + i * 3] = (byte)(fields[i] >> 14); }
        return [SysexProtocol.Frame(d.ModelByte, 0x74, 62, 1, 1, 2), SysexProtocol.Frame(d.ModelByte, 0x75, packed),
            SysexProtocol.Frame(d.ModelByte, 0x75, 1, 0, 123, 0, 0), SysexProtocol.Frame(d.ModelByte, 0x76)];
    }
    private sealed class Bits
    {
        private readonly List<int> bits = [];
        public void Add(int value, int width) { for (int i = width - 1; i >= 0; i--) bits.Add((value >> i) & 1); }
        public byte[] Bytes()
        {
            var data = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++) data[i / 8] |= (byte)(bits[i] << (7 - i % 8)); return data;
        }
    }
}
sealed class FakeMidi : IMidiTransport
{
    public event Action<byte[]>? Received;
    public event Action<Exception>? Faulted;
    public List<byte[]> Sent { get; } = [];
    public Action<byte[], FakeMidi>? Reply { get; set; }
    public Task SendReadAsync(byte[] request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!SysexProtocol.IsReadRequest(request)) throw new InvalidOperationException("Non-read request.");
        Sent.Add(request.ToArray()); Reply?.Invoke(request, this); return Task.CompletedTask;
    }
    public void Emit(byte[] data) => Received?.Invoke(data);
    public void Error(Exception error) => Faulted?.Invoke(error);
    public void Fragment(byte[] bytes)
    { for (int offset = 0; offset < bytes.Length; offset += 137) Emit(bytes.Skip(offset).Take(137).ToArray()); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
