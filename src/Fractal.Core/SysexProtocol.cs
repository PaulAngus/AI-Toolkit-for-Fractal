// Adapted from PresetNameSync.Core; model addressing generalized in toolkit release 0.2.0.
using System.Text;
namespace Fractal.Core;

public static class SysexProtocol
{
    public static byte[] Frame(byte model, byte function, params byte[] payload)
    {
        if (model > 127 || function > 127 || payload.Any(b => b > 127))
            throw new ArgumentException("SysEx payload must contain septets.");
        byte[] frame = [0xf0, 0, 1, 0x74, model, function, ..payload, 0, 0xf7];
        frame[^2] = ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
        return frame;
    }
    public static byte ComputeChecksum(ReadOnlySpan<byte> data)
    {
        int xor = 0; foreach (byte b in data) xor ^= b; return (byte)(xor & 127);
    }
    public static bool ValidFrame(ReadOnlySpan<byte> frame, byte model, byte function, int? length = null)
    {
        if (frame.Length < 8 || (length.HasValue && frame.Length != length.Value) ||
            frame[0] != 0xf0 || frame[1] != 0 || frame[2] != 1 || frame[3] != 0x74 ||
            frame[4] != model || frame[5] != function || frame[^1] != 0xf7) return false;
        for (int i = 1; i < frame.Length - 1; i++) if (frame[i] > 127) return false;
        return frame[^2] == ComputeChecksum(frame[..^2]);
    }
    public static byte[] IdentityQuery() => Frame(0x7f, 0);
    public static byte[] FirmwareQuery(DeviceDefinition d) => Frame(d.ModelByte, 8);
    public static byte[] PresetNameQuery(DeviceDefinition d, int? slot = null)
    {
        if (slot.HasValue) d.ValidateSlot(slot.Value);
        int address = slot ?? 16383;
        return Frame(d.ModelByte, 0x0d, (byte)(address & 127), (byte)(address >> 7));
    }
    public static byte[] CurrentSceneQuery(DeviceDefinition d) => Frame(d.ModelByte, 0x0c, 127);
    public static byte[] SceneNameQuery(DeviceDefinition d, int scene)
    {
        if (scene is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(scene));
        return Frame(d.ModelByte, 0x0e, (byte)(scene - 1));
    }
    public static byte[] SavedQuery(DeviceDefinition d, int slot)
    {
        d.ValidateSlot(slot);
        return Frame(d.ModelByte, 3, (byte)(slot >> 7), (byte)(slot & 127), 0);
    }
    public static byte[] SceneMidiQuery(DeviceDefinition d) => Frame(d.ModelByte, 0x1f, 62, 1);
    public static byte[] BlockParametersQuery(DeviceDefinition d, int effectId) => effectId is >= 1 and <= 1000
        ? Frame(d.ModelByte, 0x1f, (byte)(effectId & 127), (byte)(effectId >> 7)) : throw new ArgumentOutOfRangeException(nameof(effectId));
    public static byte[] EditBufferQuery(DeviceDefinition d) => Frame(d.ModelByte, 3, 127, 127, 0);
    public static byte[] BlockStatusQuery(DeviceDefinition d) => Frame(d.ModelByte, 0x13);
    public static string ReadName(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0); if (end < 0) end = bytes.Length;
        return Encoding.ASCII.GetString(bytes[..end]).TrimEnd(' ');
    }
    public static int PresetAddress(byte[] frame) => frame[6] | frame[7] << 7;
    /// <summary>Closed allowlist: no editing, navigation, save, PC/CC or arbitrary send.</summary>
    public static bool IsReadRequest(byte[] frame)
    {
        if (frame.Length < 8 || !ValidFrame(frame, frame[4], frame[5])) return false;
        if (frame[4] == 0x7f) return frame[5] == 0 && frame.Length == 8;
        if (frame[4] is not (0x10 or 0x11 or 0x12)) return false;
        int max = frame[4] == 0x10 ? 1023 : 511;
        return frame[5] switch
        {
            8 => frame.Length == 8,
            1 => AmpRoster.IsQuery(frame),
            0x13 => frame.Length == 8,
            0x0f => frame.Length == 9 && frame[6] == 127,
            0x0d => frame.Length == 10 && (PresetAddress(frame) <= max || PresetAddress(frame) == 16383),
            0x0c => frame.Length == 9 && frame[6] == 127,
            0x0e => frame.Length == 9 && frame[6] < 8,
            3 => frame.Length == 11 && ((frame[6] << 7 | frame[7]) <= max || frame[6] == 127 && frame[7] == 127) && frame[8] == 0,
            0x1f => frame.Length == 10 && (frame[6] | frame[7] << 7) is >= 1 and <= 1000,
            _ => false
        };
    }
}
