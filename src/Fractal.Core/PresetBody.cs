// Read layouts adapted from PresetMaestro.FractalIndex and credited Gen-3 format research.
using System.Buffers.Binary;
using System.Security.Cryptography;
namespace Fractal.Core;

public sealed record GridCell(int Row, int Column, int EffectId, int RouteFlag)
{
    public string? Name { get; init; }
    public string Kind => EffectId == 0 ? "empty" : EffectId is >= 1024 and <= 1029 ? "shunt" : Name == null ? "unknown" : "block";
}
public sealed record AmpChannel(string Channel, int ModelId);
public sealed record AmpScene(int Number, string Channel, bool Bypassed);
public sealed record AmpBlock(int EffectId, AmpChannel[] Channels, AmpScene[] Scenes);
public sealed record BlockRecord(int Offset, int Columns, int Rows, int[] HeaderWords, int[] ParameterWords);
public sealed record MidiRawFields(int Type, int Channel, int Controller, int Value);
public sealed record MidiMessage(int Slot, string Type, int? Channel, int? Value, int? Controller = null, MidiRawFields? Raw = null);
public sealed record SceneSnapshot(int Number, string Name, MidiMessage[] Messages);
public sealed record PresetSnapshot(int Slot, string Name, string ContentSha256, string BodySha256,
    bool Populated, string? TopLeftBlock, bool SceneMidiPresent, string SceneMidiDecodeStatus,
    int[] BodyHeaderWords, GridCell[] Grid, AmpBlock[] Amps, SceneSnapshot[] Scenes,
    BlockRecord[] BlockRecords, int? OpaqueTailOffset, string? OpaqueTailHex, string[] Limitations);
internal sealed record PresetBodyLayout(GridCell[] Grid, BlockRecord[] Records, int? OpaqueTailOffset);

public static class PresetBody
{
    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    // Shared structural reader. Semantic readers must establish instance identity separately.
    internal static PresetBodyLayout ReadLayout(DeviceDefinition d, byte[] body)
    {
        int Word(int off)
        {
            if (off < 0 || off + 2 > body.Length) throw new InvalidDataException("Truncated preset body.");
            return BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(off, 2));
        }
        if (body.Length < 0x104 + d.GridRows * d.GridColumns * 4)
            throw new InvalidDataException("Preset body is too short for this model's grid.");
        List<GridCell> grid = [];
        for (int col = 0; col < d.GridColumns; col++) for (int row = 0; row < d.GridRows; row++)
        {
            int off = 0x104 + (col * d.GridRows + row) * 4;
            grid.Add(new(row + 1, col + 1, Word(off), Word(off + 2)) { Name = BlockCatalogue.GridName(d.Model, Word(off)) });
        }
        List<BlockRecord> records = [];
        int? opaqueTailOffset = null;
        int start = -1;
        for (int off = 0x200; off + 130 <= body.Length; off += 2)
            if (Word(off + 30) == 25 && Word(off + 32) == 1 && Word(off + 126) == 25 && Word(off + 128) == 1)
            { start = off; break; }
        if (start >= 0)
        {
            for (int pos = start; pos + 46 <= body.Length;)
            {
                int cols = Word(pos + 30), rows = Word(pos + 32);
                if (cols == 0 || rows == 0) break;
                // FM9 12.00 saved images end with a 350x1-marked 736-byte tail.
                // It has a different layout from effect records; preserve it whole.
                // Exact shape/position required. Other model tail layouts stay unqualified.
                if (d.OpaqueTailColumns == cols && rows == 1 && body.Length - pos == d.OpaqueTailBytes)
                { opaqueTailOffset = pos; break; }
                if (cols > 500 || rows > 8) throw new InvalidDataException("Unsupported block record shape.");
                int size = (23 + cols * rows) * 2;
                if (pos + size > body.Length) throw new InvalidDataException("Truncated block record.");
                records.Add(new(pos, cols, rows, Enumerable.Range(0, 23).Select(i => Word(pos + i * 2)).ToArray(),
                    Enumerable.Range(0, cols * rows).Select(i => Word(pos + 46 + i * 2)).ToArray()));
                pos += size;
            }
        }
        return new(grid.ToArray(), records.ToArray(), opaqueTailOffset);
    }
    public static PresetSnapshot Decode(DeviceDefinition d, StoredPresetImage image)
    {
        byte[] body = image.Body;
        int Word(int off) => BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(off, 2));
        var layout = ReadLayout(d, body);
        var grid = layout.Grid;
        var records = layout.Records;
        var opaqueTailOffset = layout.OpaqueTailOffset;
        int[] ampIds = grid.Where(c => c.EffectId is 58 or 59).Select(c => c.EffectId).Order().ToArray();
        if (ampIds.Length > d.MaxAmps || ampIds.Distinct().Count() != ampIds.Length)
            throw new InvalidDataException("Invalid or duplicate Amp grid instances for model.");
        bool midi = grid.Any(c => c.EffectId == 190);
        if (grid.Count(c => c.EffectId == 190) > 1) throw new InvalidDataException("Duplicate Scene MIDI instance.");
        var ampRecords = records.Where(r => r.Columns == d.AmpColumns && r.Rows == 4).ToArray();
        // Never silently claim no Amp when firmware changes its record layout.
        if (ampRecords.Length != ampIds.Length) throw new InvalidDataException("Amp record/grid mismatch; body layout needs qualification.");
        List<AmpBlock> amps = [];
        for (int i = 0; i < ampIds.Length; i++)
        {
            BlockRecord r = ampRecords[i];
            var channels = Enumerable.Range(0, 4).Select(ch => new AmpChannel(((char)('A' + ch)).ToString(),
                d.AmpTypeWord >= 0 ? r.ParameterWords[ch * r.Columns + d.AmpTypeWord] :
                ch == 0 ? r.HeaderWords[17] : r.ParameterWords[(ch - 1) * r.Columns + r.Columns - 6])).ToArray();
            List<AmpScene> scenes = [];
            for (int scene = 0; scene < 8; scene++)
            {
                int channel = Word(r.Offset + (scene - 1) * 2), bypass = r.HeaderWords[7 + scene];
                if (channel > 3 || bypass > 1) throw new InvalidDataException("Invalid Amp scene channel/bypass.");
                scenes.Add(new(scene + 1, ((char)('A' + channel)).ToString(), bypass == 1));
            }
            amps.Add(new(ampIds[i], channels, scenes.ToArray()));
        }
        string status = midi ? "unsupported-saved-table-layout" : "absent";
        string[] limitations = [];
        var sceneSnapshots = Enumerable.Range(0, 8).Select(s => new SceneSnapshot(s + 1, image.SceneNames[s], [])).ToArray();
        if (midi)
        {
            try
            {
                int[] table = SceneMidiTable.SavedFields(d, records.ToArray());
                var decoded = SceneMidiTable.Decode(d, table);
                sceneSnapshots = sceneSnapshots.Select(s => s with { Messages = decoded[s.Number - 1] }).ToArray();
                status = "decoded";
            }
            catch (InvalidDataException error)
            { limitations = [$"Scene MIDI could not be decoded: {error.Message} Planner must report a conflict."]; }
        }
        int top = grid[0].EffectId;
        string? topLabel = top == 0 ? null : grid[0].Name ?? $"Effect {top}";
        return new(image.Slot, image.Name, Sha256(image.RawImage), Sha256(body),
            grid.Any(c => c.EffectId is > 0 and <= 1000), topLabel, midi, status,
            [Word(0), Word(2)], grid.ToArray(), amps.ToArray(),
            sceneSnapshots,
            records.ToArray(), opaqueTailOffset,
            opaqueTailOffset.HasValue ? Convert.ToHexStringLower(body.AsSpan(opaqueTailOffset.Value)) : null,
            limitations);
    }
}
