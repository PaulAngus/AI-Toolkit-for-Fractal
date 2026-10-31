// Modified from PresetNameSync.Core; see protocol-release.json for source hash and changes.
using System.Buffers.Binary;

namespace Fractal.Core;

/// <summary>Read-only 0x77/78/79 dump decoder. Sources/evidence: docs/runtime.md and docs/protocol-release.json.</summary>
public sealed class StoredPresetDecoder
{
    private readonly byte _modelByte;
    private readonly int _maxSlot;
    private readonly int _chunkCount;
    private int? _slot;
    private readonly List<byte> _raw = [];

    public StoredPresetDecoder(DeviceDefinition device)
    {
        _modelByte = device.ModelByte;
        _maxSlot = device.PresetCapacity - 1;
        _chunkCount = device.DumpChunks;
    }

    public StoredPresetImage? AcceptImage(byte[] frame, int? expectedSlot = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Length < 6 || frame[5] is not (0x77 or 0x78 or 0x79))
        {
            return null;
        }

        int length = frame[5] switch { 0x77 => 13, 0x78 => 3082, _ => 11 };
        if (!SysexProtocol.ValidFrame(frame, _modelByte, frame[5], length))
        {
            throw new InvalidDataException("Invalid preset dump frame/checksum.");
        }

        if (frame[5] == 0x77)
        {
            _raw.Clear(); _slot = null;
            int slot = frame[6] << 7 | frame[7];
            if (slot > _maxSlot)
            {
                throw new InvalidDataException("Invalid stored preset slot.");
            }

            if (expectedSlot.HasValue && slot != expectedSlot)
                throw new InvalidDataException("Saved reply has the wrong preset address.");
            _slot = slot;

            return null;
        }
        if (_slot == null)
        {
            return null;
        }

        if (frame[5] == 0x78)
        {
            if (_raw.Count >= _chunkCount * 2048)
            {
                throw new InvalidDataException("Too many preset dump chunks.");
            }

            // The first two payload fields are opaque: FM9 12.00 repeats them.
            // They are not established sequence numbers. Transfer size, footer XOR
            // and the complete raw CRC detect missing/extra/corrupt data instead.
            _raw.AddRange(UnpackChunk(frame));
            return null;
        }
        int address = _slot.Value; _slot = null;
        byte[] raw = _raw.ToArray(); _raw.Clear();
        PresetPayload payload = DecodePayload(raw, frame, _chunkCount);
        return new StoredPresetImage(address, payload.Name, payload.SceneNames, payload.RawImage, payload.Body);
    }
    // Shared with file inspection; a backup header is retained without inventing a saved-slot address.
    internal static byte[] UnpackChunk(byte[] frame)
    {
        var raw = new byte[2048];
        for (int i = 8, offset = 0; i < frame.Length - 2; i += 3, offset += 2)
        {
            if (frame[i + 2] > 3) throw new InvalidDataException("Invalid packed 16-bit word.");
            int word = frame[i] | frame[i + 1] << 7 | frame[i + 2] << 14;
            raw[offset] = (byte)word; raw[offset + 1] = (byte)(word >> 8);
        }
        return raw;
    }
    internal static PresetPayload DecodePayload(byte[] raw, byte[] footer, int chunkCount)
    {
        if (raw.Length != chunkCount * 2048 || Word(raw, 2) != 0xaa55)
        {
            throw new InvalidDataException("Unsupported or incomplete preset image.");
        }

        int xor = 0; for (int i = 0; i < raw.Length; i += 2)
        {
            xor ^= Word(raw, i);
        }

        if (footer[8] > 3 || xor != (footer[6] | footer[7] << 7 | footer[8] << 14))
        {
            throw new InvalidDataException("Preset footer checksum mismatch.");
        }

        if (Word(raw, 4) != ComputeCrc(raw))
        {
            throw new InvalidDataException("Preset CRC mismatch.");
        }

        int compressed = Word(raw, 0x4a), decompressed = Word(raw, 0x48);
        if (compressed == 0 || compressed > raw.Length - 0x4c || decompressed < 260)
        {
            throw new InvalidDataException("Invalid compressed preset sizes.");
        }

        byte[] body = Expand(raw.AsSpan(0x4c, compressed).ToArray(), decompressed);
        string[] names = new string[8];
        for (int i = 0; i < 8; i++)
        {
            names[i] = SysexProtocol.ReadName(body.AsSpan(4 + 32 * i, 32));
        }

        return new PresetPayload(SysexProtocol.ReadName(raw.AsSpan(8, 32)), names, raw, body);
    }
    private static int Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    public static ushort ComputeCrc(ReadOnlySpan<byte> raw)
    {
        int crc = 0xaa55;
        for (int i = 0; i < raw.Length; i++)
        {
            crc ^= (i is 4 or 5 ? 0 : raw[i]) << 8;
            for (int b = 0; b < 8; b++)
            {
                crc = ((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0)) & 0xffff;
            }
        }
        return (ushort)crc;
    }
    private sealed record Node(int Symbol, Node? Left = null, Node? Right = null);
    private static byte[] Expand(byte[] data, int size)
    {
        int position = 0, nodes = 0;
        int Bit()
        {
            if (position >= data.Length * 8)
            {
                throw new InvalidDataException("Truncated Huffman data.");
            }

            int p = position++; return (data[p / 8] >> (7 - p % 8)) & 1;
        }
        Node Tree(int depth)
        {
            if (++nodes > 511 || depth > 255)
            {
                throw new InvalidDataException("Invalid Huffman tree.");
            }

            if (Bit() == 1)
            {
                int symbol = 0; for (int i = 0; i < 8; i++)
                {
                    symbol = symbol << 1 | Bit();
                }

                return new Node(symbol);
            }
            return new Node(-1, Tree(depth + 1), Tree(depth + 1));
        }
        Node root = Tree(0);
        var output = new byte[size];
        for (int i = 0; i < size; i++)
        {
            Node node = root;
            while (node.Symbol < 0)
            {
                node = Bit() == 0 ? node.Left! : node.Right!;
            }

            output[i] = (byte)node.Symbol;
        }
        return output;
    }
}

public sealed record StoredPresetImage(int Slot, string Name, string[] SceneNames, byte[] RawImage, byte[] Body);
internal sealed record PresetPayload(string Name, string[] SceneNames, byte[] RawImage, byte[] Body);
