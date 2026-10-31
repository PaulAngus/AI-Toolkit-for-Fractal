namespace Fractal.Core;

// Banks are independently recorded in each model's editor catalog. Saved word 17
// and the live 74/75/76 transfer are hardware-qualified on FM9 12.00. Other model
// saved profiles remain provisional; shape checks do not establish hardware coverage.
public sealed record SceneMidiLayout(int Columns, int Rows, int FirstWord,
    int TypeBank, int ChannelBank, int ControllerBank, int ValueBank);
public sealed record SceneMidiCapture(int[] Fields, int ExtraWord, int[] SectionMetadata, byte[] Sysex);

public static class SceneMidiTable
{
    public static int[] SavedFields(DeviceDefinition device, BlockRecord[] records)
    {
        var layout = device.SceneMidi ?? throw new InvalidDataException("No Scene MIDI layout for this definition.");
        var matching = records.Where(r => r.Columns == layout.Columns && r.Rows == layout.Rows).ToArray();
        if (matching.Length != 1) throw new InvalidDataException("Scene MIDI record missing or ambiguous.");
        var record = matching[0];
        if (record.HeaderWords.Length != 23 || record.ParameterWords.Length != record.Columns * record.Rows ||
            record.HeaderWords[15] != record.Columns || record.HeaderWords[16] != record.Rows)
            throw new InvalidDataException("Inconsistent Scene MIDI record structure.");
        var words = matching[0].HeaderWords.Concat(matching[0].ParameterWords).ToArray();
        if (layout.FirstWord < 0 || layout.FirstWord + 256 > words.Length)
            throw new InvalidDataException("Truncated Scene MIDI table.");
        return words.Skip(layout.FirstWord).Take(256).ToArray();
    }
    public static MidiMessage[][] Decode(DeviceDefinition device, int[] fields)
    {
        var l = device.SceneMidi ?? throw new InvalidDataException("No Scene MIDI layout for this definition.");
        if (fields.Length != 256 || fields.Any(v => v is < 0 or > 65535))
            throw new InvalidDataException("Scene MIDI requires 256 unsigned 16-bit fields.");
        if (new[] { l.TypeBank, l.ChannelBank, l.ControllerBank, l.ValueBank }.Order().SequenceEqual(new[] { 0, 64, 128, 192 }) is false)
            throw new InvalidDataException("Unsupported Scene MIDI field banks.");
        return Enumerable.Range(0, 8).Select(scene => Enumerable.Range(0, 8).Select(slot =>
        {
            int i = scene * 8 + slot;
            var raw = new MidiRawFields(fields[l.TypeBank + i], fields[l.ChannelBank + i],
                fields[l.ControllerBank + i], fields[l.ValueBank + i]);
            if (raw.Type == 0) return new MidiMessage(slot + 1, "Off", null, null, Raw: raw);
            if (raw.Type is not (1 or 2) || raw.Channel > 15 || raw.Value > 127 || raw.Type == 1 && raw.Controller > 127)
                throw new InvalidDataException($"Unsupported Scene MIDI fields at scene {scene + 1}, message {slot + 1}.");
            return new MidiMessage(slot + 1, raw.Type == 1 ? "CC" : "PC", raw.Channel + 1, raw.Value,
                raw.Type == 1 ? raw.Controller : null, raw);
        }).ToArray()).ToArray();
    }
}

/// <summary>Strict, bounded current-block transfer; sections are ordered, never merged across headers.</summary>
public sealed class SceneMidiTableDecoder(DeviceDefinition device)
{
    private bool started;
    private bool completed;
    private int section;
    private readonly List<byte> wire = [];
    private readonly List<int> fields = [];
    private readonly List<int> metadata = [];
    private int extraWord;
    public SceneMidiCapture? Accept(byte[] frame)
    {
        if (completed) throw new InvalidDataException("Extra Scene MIDI transfer frame.");
        if (frame.Length < 6 || frame[4] != device.ModelByte || frame[5] is not (0x74 or 0x75 or 0x76)) return null;
        byte function = frame[5];
        if (function == 0x74)
        {
            if (!SysexProtocol.ValidFrame(frame, device.ModelByte, function, 12)) throw new InvalidDataException("Invalid Scene MIDI header.");
            if ((frame[6] | frame[7] << 7) != 190)
            {
                if (started) throw new InvalidDataException("Interleaved block transfer; reconnect before another read.");
                return null;
            }
            if (started || (frame[8] | frame[9] << 7) != 257) throw new InvalidDataException("Repeated or unsupported Scene MIDI header.");
            started = true;
        }
        if (!started) return null;
        if (!SysexProtocol.ValidFrame(frame, device.ModelByte, function)) throw new InvalidDataException("Invalid Scene MIDI frame checksum.");
        wire.AddRange(frame);
        if (function == 0x75)
        {
            int count = section == 0 ? 256 : 1;
            if (section > 1 || frame.Length != 10 + count * 3 || frame[6] != section)
                throw new InvalidDataException("Missing, repeated or unsupported Scene MIDI section.");
            // Byte 7 is opaque section metadata, not an address or a constant.
            metadata.Add(frame[7]);
            for (int i = 0; i < count; i++)
            {
                int off = 8 + i * 3;
                if (frame[off + 2] > 3) throw new InvalidDataException("Scene MIDI field exceeds 16 bits.");
                int word = frame[off] | frame[off + 1] << 7 | frame[off + 2] << 14;
                if (section == 0) fields.Add(word); else extraWord = word;
            }
            section++;
        }
        if (function != 0x76) return null;
        if (frame.Length != 8 || section != 2) throw new InvalidDataException("Incomplete Scene MIDI transfer.");
        completed = true;
        return new(fields.ToArray(), extraWord, metadata.ToArray(), wire.ToArray());
    }
}
