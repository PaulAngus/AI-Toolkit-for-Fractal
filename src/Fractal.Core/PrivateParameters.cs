namespace Fractal.Core;

public sealed record MappedParameter(int ParameterId, int RawValue, EditorParameterMapping[] Mappings);
public sealed record BlockParameterCapture(int EffectId, BlockIdentity Block, MappedParameter[] Parameters,
    int? HeaderMetadata, int[] SectionMetadata, byte[] Sysex, string Qualification, string SourceId, bool HardwareQualified = false);

/// <summary>Private Gen3 table positions are editor IDs, never saved-file offsets or calibrated units.</summary>
public sealed class BlockParameterDecoder(DeviceDefinition device, int? expectedEffectId = null)
{
    private int? effectId;
    private int count;
    private int? headerMetadata;
    private bool completed;
    private readonly List<int> values = [], metadata = [];
    private readonly List<byte> wire = [];
    public bool Incomplete => effectId != null && !completed;
    public byte[] Received => wire.ToArray();
    // Block labels and editor catalogue family labels use these documented synonyms.
    internal static string? ParameterFamily(BlockIdentity block) => block.Family switch
    {
        "Gate/Expander" => "Gate", "Megatap" => "Megatap Delay", "Tremolo/Panner" => "Tremolo", _ => block.Family
    };
    internal static EditorParameterMapping[] Mappings(DeviceDefinition device, int effectId, int parameterId)
    {
        string? family = ParameterFamily(BlockCatalogue.Resolve(device.Model, effectId));
        return family == null ? [] : AudioParameterMappings.ForModel(device.Model, family).Entries.Where(p => p.ParameterId == parameterId).ToArray();
    }
    public BlockParameterCapture? Accept(byte[] frame)
    {
        if (frame.Length < 6 || frame[4] != device.ModelByte || frame[5] is not (0x74 or 0x75 or 0x76)) return null;
        if (completed) throw new InvalidDataException("Extra private table frame.");
        if (!SysexProtocol.ValidFrame(frame, device.ModelByte, frame[5])) throw new InvalidDataException("Invalid private table checksum/framing.");
        if (frame[5] == 0x74)
        {
            if (frame.Length is not (12 or 13)) throw new InvalidDataException("Unsupported private table header length.");
            int address = frame[6] | frame[7] << 7;
            if (effectId != null) throw new InvalidDataException("Repeated/interleaved private table header.");
            if (expectedEffectId != null && address != expectedEffectId) return null;
            count = frame[8] | frame[9] << 7;
            if (address is < 1 or > 1000 || count is < 1 or > 4096) throw new InvalidDataException("Private table address/count out of bounds.");
            effectId = address;
            headerMetadata = frame.Length == 13 ? frame[10] : null;
        }
        if (effectId == null) return null;
        wire.AddRange(frame);
        if (frame[5] == 0x75)
        {
            int fields = Math.Min(256, count - values.Count);
            int pageCount = frame[6] | frame[7] << 7;
            if (fields <= 0 || pageCount != fields || frame.Length != 10 + 3 * fields)
                throw new InvalidDataException("Missing/repeated/private table page or inconsistent field count.");
            metadata.Add(pageCount);
            for (int offset = 8; offset < frame.Length - 2; offset += 3)
            {
                if (frame[offset + 2] > 3) throw new InvalidDataException("Private table field exceeds 16 bits.");
                values.Add(frame[offset] | frame[offset + 1] << 7 | frame[offset + 2] << 14);
            }
        }
        if (frame[5] != 0x76) return null;
        if (frame.Length != 8 || values.Count != count) throw new InvalidDataException("Incomplete private table.");
        completed = true;
        var block = BlockCatalogue.Resolve(device.Model, effectId.Value);
        string? family = ParameterFamily(block);
        var mappings = family == null ? [] : AudioParameterMappings.ForModel(device.Model, family).Entries;
        return new(effectId.Value, block, values.Select((v, i) => new MappedParameter(i, v, mappings.Where(p => p.ParameterId == i).ToArray())).ToArray(),
            headerMetadata, metadata.ToArray(), wire.ToArray(), "open-implementation positional editor IDs; raw unsigned words; page counts and terminal do not distinguish same-length page repetition; no calibrated units, channel/preset identity or saved-state claim",
            "gen3-editor-actions-pinned");
    }
}

public static class PrivateParameters
{
    // Subactions share fn01 with editor operations. Recognize only these source-supported reply shapes.
    public static object? DecodeEcho(DeviceDefinition device, byte[] frame)
    {
        if (frame.Length < 8 || frame[5] != 1 || frame[7] != 0 || frame[6] is not (9 or 0x52 or 0x1f)) return null;
        if (!SysexProtocol.ValidFrame(frame, device.ModelByte, 1) || frame.Length < 23 || frame[16] > 15)
            throw new InvalidDataException("Invalid private parameter echo.");
        int effect = frame[8] | frame[9] << 7, parameter = frame[10] | frame[11] << 7;
        if (effect is < 1 or > 1000) throw new InvalidDataException("Invalid private parameter effect ID.");
        uint bits = 0;
        for (int i = 0; i < 5; i++) bits |= (uint)frame[12 + i] << (7 * i);
        float value = BitConverter.UInt32BitsToSingle(bits);
        if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite private parameter value.");
        int length = frame[19] | frame[20] << 7;
        if (length > 256 || frame.Length != 23 + length + (length + 6) / 7)
            throw new InvalidDataException("Invalid private parameter display length.");
        var bytes = new List<byte>();
        int cursor = 21;
        while (bytes.Count < length)
        {
            int n = Math.Min(7, length - bytes.Count);
            for (int i = 0; i < n; i++) bytes.Add((byte)(((frame[cursor + i] << (i + 1)) | (frame[cursor + i + 1] >> (6 - i))) & 255));
            if ((frame[cursor + n] & ((1 << (7 - n)) - 1)) != 0) throw new InvalidDataException("Nonzero private display padding.");
            cursor += n + 1;
        }
        return new { effect_id = effect, block = BlockCatalogue.Resolve(device.Model, effect), parameter_id = parameter,
            mappings = BlockParameterDecoder.Mappings(device, effect, parameter), action = frame[6], raw_float_bits = bits,
            raw_float_value = value, type_ordinal = (int?)null, modifier_raw = frame[17] | frame[18] << 7,
            display = System.Text.Encoding.Latin1.GetString(bytes.ToArray()).TrimEnd('\0'), display_encoding = "source byte-string (Latin-1 rendering)",
            qualification = "private open-implementation format; raw float has no inferred physical units; discrete echoes and display replies may carry zero, which does not establish a type ordinal",
            source_id = "gen3-editor-actions-pinned", hardware_qualified = false };
    }
}

/// <summary>One observer per subscription. Invalid bursts are quarantined until the next header.</summary>
public sealed class DeviceNotificationObserver(DeviceDefinition device)
{
    private BlockParameterDecoder? table;
    public bool Incomplete => table?.Incomplete == true;
    public DeviceNotification Observe(byte[] frame)
    {
        var decoded = DeviceNotifications.Decode(device, frame);
        if (frame.Length < 6 || frame[4] != device.ModelByte || frame[5] is not (0x74 or 0x75 or 0x76)) return decoded;
        try
        {
            if (!decoded.Valid) { table = null; return decoded; }
            if (frame[5] == 0x74)
            {
                bool interrupted = Incomplete;
                table = new(device);
                table.Accept(frame);
                if (interrupted) return decoded with { Valid = false, Error = "Previous private table interrupted by a new header; new transfer started." };
                return decoded;
            }
            if (table == null) return decoded with { Valid = false, Error = "Private section/end without a matching header." };
            var result = table.Accept(frame);
            if (result == null) return decoded;
            table = null;
            return decoded with { Kind = "private-block-parameters", Value = result };
        }
        catch (InvalidDataException e) { table = null; return decoded with { Valid = false, Error = e.Message }; }
    }
}
