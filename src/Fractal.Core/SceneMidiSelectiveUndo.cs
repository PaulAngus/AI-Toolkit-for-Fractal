using System.Buffers.Binary;

namespace Fractal.Core;

public sealed record SceneMidiSelectiveUndoResult(PresetEditPlan Plan, string SourceSha256,
    string HistoricalBaseSha256, string HistoricalResultSha256, int[] OwnedRows, int[] AlreadyRestoredRows,
    string SourceKind = "general-edit-receipt");

/// <summary>Replan a completed general-edit Scene MIDI receipt against a new saved capture.
/// Only exact, receipt-owned message rows are reverted; the normal editor owns execution.</summary>
public static class SceneMidiSelectiveUndo
{
    public static SceneMidiSelectiveUndoResult Create(string receiptDirectory, string receiptSha256, byte[] currentWire)
    {
        var (historical, _) = PresetEditExecution.PrepareUndo(receiptDirectory, receiptSha256);
        if (!historical.OnlySceneMidiOperations)
            throw new InvalidDataException("Selective undo requires a receipt containing only Scene MIDI message edits.");
        if (historical.Persistence != "save") throw new InvalidDataException("Selective undo needs a completed saved edit.");
        return CreateCore(historical.Device, historical.Firmware, historical.Slot, historical.Baseline,
            historical.Expected, currentWire, receiptSha256, "general-edit-receipt");
    }

    public static SceneMidiSelectiveUndoResult CreateFromRecovery(SceneMidiRecoveryStore store, int slot, byte[] currentWire)
    {
        var (original, result) = store.CompletedImagesForSelectiveUndo(slot);
        return CreateCore(store.Device, store.Firmware, slot, original, result, currentWire,
            store.StateHash, "scene-midi-recovery-store");
    }

    private static SceneMidiSelectiveUndoResult CreateCore(DeviceDefinition device, string firmware, int slot,
        byte[] originalWire, byte[] resultWire, byte[] currentWire, string sourceHash, string sourceKind)
    {
        var original = SnapshotExport.DecodeFile(device, originalWire);
        var result = SnapshotExport.DecodeFile(device, resultWire);
        var current = SnapshotExport.DecodeFile(device, currentWire);
        if (original.Image.Slot != slot || result.Image.Slot != slot || current.Image.Slot != slot)
            throw new InvalidDataException("Selective undo requires the exact saved target of a completed edit.");
        if (new[] { original, result, current }.Any(x => PresetBody.Decode(device, x.Image).SceneMidiDecodeStatus != "decoded"))
            throw new InvalidDataException("Selective undo requires three fully decoded Scene MIDI tables.");
        var first = PresetEditProjection.Decode(device, firmware, originalWire);
        var second = PresetEditProjection.Decode(device, firmware, resultWire);
        var latest = PresetEditProjection.Decode(device, firmware, currentWire);
        SavedBlockRead Midi(PresetEditReadReport report)
        {
            var matches = report.Blocks.Where(b => b.EffectId == 190 && b.Record != null && b.Cells.Length == 1).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Selective undo requires one existing exact Scene MIDI instance.");
            return matches[0];
        }
        var a = Midi(first); var b = Midi(second); var c = Midi(latest);
        var layout = device.SceneMidi ?? throw new InvalidDataException("Scene MIDI layout is unavailable.");
        bool Shape(SavedBlockRead x) => x.Record!.Columns == layout.Columns && x.Record.Rows == layout.Rows;
        if (!Shape(a) || !Shape(b) || !Shape(c) || a.Record!.ShapeOffset != b.Record!.ShapeOffset ||
            b.Record.ShapeOffset != c.Record!.ShapeOffset || !a.Cells.SequenceEqual(b.Cells) || !b.Cells.SequenceEqual(c.Cells))
            throw new InvalidDataException("Scene MIDI block layout or placement changed since the receipt.");
        byte[] oldBody = original.Image.Body, resultBody = result.Image.Body, nowBody = current.Image.Body;
        int start = a.Record.ShapeOffset + layout.FirstWord * 2, end = start + 512;
        if (oldBody.Length != resultBody.Length || oldBody.Length != nowBody.Length || end > oldBody.Length ||
            !oldBody.AsSpan(0, start).SequenceEqual(resultBody.AsSpan(0, start)) ||
            !oldBody.AsSpan(end).SequenceEqual(resultBody.AsSpan(end)))
            throw new InvalidDataException("Receipt contains changes outside the complete Scene MIDI message table.");
        int[] offsets = [layout.TypeBank, layout.ChannelBank, layout.ControllerBank, layout.ValueBank];
        int[] Words(byte[] body, int row) => offsets.Select(bank =>
            (int)BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(start + 2 * (bank + row), 2))).ToArray();
        var owned = new List<int>(); var restored = new List<int>();
        var expectedRows = new List<int>();
        byte[] merged = nowBody.ToArray();
        for (int row = 0; row < 64; row++)
        {
            int[] was = Words(oldBody, row), became = Words(resultBody, row);
            if (was.SequenceEqual(became)) continue;
            owned.Add(row + 1);
            int[] actual = Words(nowBody, row);
            if (!actual.SequenceEqual(became) && !actual.SequenceEqual(was))
                throw new InvalidDataException($"Receipt-owned Scene MIDI row {row + 1} has a later conflicting edit.");
            if (actual.SequenceEqual(was)) restored.Add(row + 1);
            expectedRows.AddRange(actual);
            for (int i = 0; i < 4; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(merged.AsSpan(start + 2 * (offsets[i] + row), 2), (ushort)was[i]);
        }
        if (owned.Count == 0) throw new InvalidDataException("Receipt contains no changed Scene MIDI message rows.");
        var operation = new PresetEditOperation("restore-scene-midi-rows", MessageRows: owned.ToArray(),
            ExpectedRowsRaw: expectedRows.ToArray(), TemplateCapture: Convert.ToBase64String(originalWire),
            TemplateSha256: PresetBody.Sha256(originalWire), TemplateFirmware: firmware);
        var request = new PresetEditRequest(1, device.Model, device.PresetCapacity, firmware,
            slot, "save", PresetBody.Sha256(currentWire), [operation]);
        var plan = PresetEditPlan.Create(request, currentWire);
        if (!plan.Executable || !SnapshotExport.DecodeFile(device, plan.Expected).Image.Body.SequenceEqual(merged))
            throw new InvalidDataException("The backup-sourced Scene MIDI editor did not restore the exact owned words; no selective undo plan was created.");
        return new(plan, sourceHash, PresetBody.Sha256(originalWire), PresetBody.Sha256(resultWire),
            owned.ToArray(), restored.ToArray(), sourceKind);
    }
}
