using System.Buffers.Binary;
namespace Fractal.Core;

/// <summary>Independent byte comparison for an owned Scene MIDI insertion. FM9 12.00 physically qualified;
/// other model profiles remain provisional. Does not serialize or restore a preset.</summary>
public static class EditorPreservation
{
    private static (int Row, int Column, int EffectId, int RouteFlag) Numeric(GridCell g) => (g.Row, g.Column, g.EffectId, g.RouteFlag);
    /// <summary>Remove the actual new instance interval, then require every existing body byte and route flag unchanged.</summary>
    public static void VerifyBlockInsertion(DeviceDefinition d, StoredPresetImage original, StoredPresetImage changed,
        int row, int column, int effectId)
    {
        _ = EditorCommand.InsertBlock(d, original.Slot, row, column, effectId);
        var before = PresetBody.Decode(d, original); var after = PresetBody.Decode(d, changed);
        var cell = before.Grid.Single(g => g.Row == row && g.Column == column);
        if (original.Slot != changed.Slot || original.Name != changed.Name || !original.SceneNames.SequenceEqual(changed.SceneNames) ||
            cell.EffectId != 0 || cell.RouteFlag != 0 || before.Grid.Any(g => g.EffectId == effectId) ||
            after.Grid.Count(g => g.EffectId == effectId) != 1 || before.Limitations.Length != 0 || after.Limitations.Length != 0 ||
            !after.Grid.Select(Numeric).SequenceEqual(before.Grid.Select(g => Numeric(g == cell ? g with { EffectId = effectId } : g))))
            throw new InvalidDataException("Inserted instance, grid, routing or existing semantics differ from intent.");
        var owned = after.BlockRecords.Where(r => r.Offset >= 12 &&
            BinaryPrimitives.ReadUInt16LittleEndian(changed.Body.AsSpan(r.Offset - 12, 2)) == effectId).ToArray();
        if (owned.Length != 1 || after.BlockRecords.Length != before.BlockRecords.Length + 1)
            throw new InvalidDataException("Inserted instance record cannot be identified uniquely.");
        var record = owned[0]; int start = record.Offset - 12, length = (23 + record.Columns * record.Rows) * 2;
        if (start + length > changed.Body.Length || changed.Body.Length != original.Body.Length + length)
            throw new InvalidDataException("Inserted instance interval is not recognized.");
        byte[] projection = changed.Body.Take(start).Concat(changed.Body.Skip(start + length)).ToArray();
        int cellOffset = 0x104 + ((column - 1) * d.GridRows + row - 1) * 4;
        original.Body.AsSpan(cellOffset, 4).CopyTo(projection.AsSpan(cellOffset, 4));
        // No default-scene normalization: it and all existing parameters/MIDI messages must remain exact.
        if (!projection.SequenceEqual(original.Body)) throw new InvalidDataException("Unrelated body bytes changed during insertion.");
        VerifyRawRepresentation(original,changed);
    }
    /// <summary>Project out exactly one isolated instance; preserve every other body byte and raw header/padding.</summary>
    public static void VerifyBlockRemoval(DeviceDefinition d, StoredPresetImage original, StoredPresetImage changed,
        int row, int column, int effectId) => VerifyBlockInsertion(d, changed, original, row, column, effectId);
    /// <summary>Compare opaque header and padding; strict decoding validates CRC/length/compressed representation.</summary>
    public static void VerifyRawRepresentation(StoredPresetImage original,StoredPresetImage changed)
    {
        if (original.RawImage.Length != changed.RawImage.Length || original.RawImage.Length < 0x4c ||
            !original.RawImage.AsSpan(0, 4).SequenceEqual(changed.RawImage.AsSpan(0, 4)) ||
            !original.RawImage.AsSpan(6, 0x48 - 6).SequenceEqual(changed.RawImage.AsSpan(6, 0x48 - 6)))
            throw new InvalidDataException("Unrelated raw preset header bytes changed.");
        int compressedEnd = 0x4c + Math.Max(BinaryPrimitives.ReadUInt16LittleEndian(original.RawImage.AsSpan(0x4a, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(changed.RawImage.AsSpan(0x4a, 2)));
        if (compressedEnd > original.RawImage.Length || !original.RawImage.AsSpan(compressedEnd).SequenceEqual(changed.RawImage.AsSpan(compressedEnd)))
            throw new InvalidDataException("Unrelated raw preset padding changed.");
    }
    public static void VerifySceneMidiInsertion(DeviceDefinition d, StoredPresetImage original, StoredPresetImage changed,
        int row, int column, int intendedSavedScene)
    {
        _ = EditorCommand.SelectCell(d, original.Slot, row, column);
        if (intendedSavedScene is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(intendedSavedScene));
        var before = PresetBody.Decode(d, original); var after = PresetBody.Decode(d, changed);
        var cell = before.Grid.Single(g => g.Row == row && g.Column == column);
        if (original.Slot != changed.Slot || original.Name != changed.Name || !original.SceneNames.SequenceEqual(changed.SceneNames) ||
            before.SceneMidiPresent || cell.EffectId != 0 || after.SceneMidiDecodeStatus != "decoded" ||
            Numeric(after.Grid.Single(g => g.Row == row && g.Column == column)) != Numeric(cell with { EffectId = 190 }) ||
            BinaryPrimitives.ReadUInt16LittleEndian(changed.Body.AsSpan(0, 2)) != intendedSavedScene - 1)
            throw new InvalidDataException("Insertion identity, placement, routing, names or saved scene differ from intent.");
        var layout = d.SceneMidi!;
        var record = after.BlockRecords.Single(r => r.Columns == layout.Columns && r.Rows == layout.Rows);
        // Shape-record offsets start 12 bytes after the instance prefix and include the next prefix in their
        // final six words. Removing the actual interval retains the complete following instance unchanged.
        int start = record.Offset - 12, length = (23 + record.Columns * record.Rows) * 2;
        if (start < 0 || start + length > changed.Body.Length || changed.Body.Length != original.Body.Length + length ||
            BinaryPrimitives.ReadUInt16LittleEndian(changed.Body.AsSpan(start, 2)) != 190)
            throw new InvalidDataException("Inserted instance interval is not recognized.");
        byte[] projection = changed.Body.Take(start).Concat(changed.Body.Skip(start + length)).ToArray();
        int cellOffset = 0x104 + ((column - 1) * d.GridRows + row - 1) * 4;
        original.Body.AsSpan(cellOffset, 4).CopyTo(projection.AsSpan(cellOffset, 4));
        original.Body.AsSpan(0, 2).CopyTo(projection.AsSpan(0, 2));
        if (!projection.SequenceEqual(original.Body)) throw new InvalidDataException("Unrelated saved body bytes changed.");
    }
}
