namespace Fractal.Core;

/// <summary>Offline inspection of one Gen-3 preset file, without inferring a restore destination.</summary>
public static class PresetFile
{
    public static byte[] AddressSaved(DeviceDefinition device, byte[] sysex, int slot)
    {
        device.ValidateSlot(slot); _ = Inspect(device, sysex);
        byte[] result = sysex.ToArray(); result[6] = (byte)(slot >> 7); result[7] = (byte)(slot & 127);
        result[11] = SysexProtocol.ComputeChecksum(result.AsSpan(0, 11));
        _ = SnapshotExport.DecodeFile(device, result); return result;
    }
    public static PresetFileInspection Inspect(DeviceDefinition device, byte[] sysex)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(sysex);
        if (sysex.Length is < 24 or > 60000)
            throw new InvalidDataException("Expected one bounded Gen-3 preset file.");

        // File framing is strict. Live assemblers may tolerate interleaved realtime bytes;
        // a backup must not silently discard garbage, extra transfers or unfinished frames.
        bool inside = false;
        foreach (byte b in sysex)
        {
            if (b == 0xf0 && !inside) inside = true;
            else if (b == 0xf7 && inside) inside = false;
            else if (!inside || b >= 0x80) throw new InvalidDataException("Invalid preset file byte framing.");
        }
        if (inside) throw new InvalidDataException("Truncated preset file.");

        byte[][] frames = new SysexAssembler().Feed(sysex).ToArray();
        if (frames.Length != device.DumpChunks + 2)
            throw new InvalidDataException("Expected exactly one complete model-sized preset transfer.");
        List<byte> raw = [];
        for (int i = 0; i < frames.Length; i++)
        {
            byte function = i == 0 ? (byte)0x77 : i == frames.Length - 1 ? (byte)0x79 : (byte)0x78;
            int length = function switch { 0x77 => 13, 0x78 => 3082, _ => 11 };
            if (!SysexProtocol.ValidFrame(frames[i], device.ModelByte, function, length))
                throw new InvalidDataException("Invalid preset file order, model, frame length or checksum.");
            if (function == 0x78) raw.AddRange(StoredPresetDecoder.UnpackChunk(frames[i]));
        }
        PresetPayload payload = StoredPresetDecoder.DecodePayload(raw.ToArray(), frames[^1], device.DumpChunks);
        return new(device.Model, frames[0].AsSpan(6, 5).ToArray(), device.DumpChunks,
            payload.Name, payload.SceneNames, payload.RawImage, payload.Body);
    }
}

// HeaderPayload is deliberately opaque. A single-preset backup need not contain
// an addressed saved-read header. Integrity does not establish upload/save semantics.
public sealed record PresetFileInspection(string Model, byte[] HeaderPayload, int ChunkCount,
    string Name, string[] SceneNames, byte[] RawImage, byte[] Body);
