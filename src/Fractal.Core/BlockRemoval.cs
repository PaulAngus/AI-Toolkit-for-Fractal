using System.Buffers.Binary;
namespace Fractal.Core;

/// <summary>One isolated, caller-resolved instance removal, bound to a complete saved baseline.</summary>
public sealed class BlockRemovalPlan
{
    public DeviceDefinition Device { get; }
    public int Slot => baseline.Slot;
    public int Row { get; }
    public int Column { get; }
    public int EffectId { get; }
    public int SavedScene { get; }
    public string BaseSha256 { get; }
    private readonly StoredPresetImage baseline;
    private StoredPresetImage? undo;
    private BlockRemovalPlan(DeviceDefinition d, SavedCapture saved, int row, int column, int id, int scene)
    { Device = d; baseline = saved.Image; Row = row; Column = column; EffectId = id; SavedScene = scene; BaseSha256 = PresetBody.Sha256(saved.Sysex); }
    public static BlockRemovalPlan Create(DeviceDefinition d, byte[] savedWire, string baseHash, int row, int column, int effectId, int savedScene, byte[]? undoWire = null)
    {
        if (d != DeviceDefinition.Create(d.Model, d.PresetCapacity)) throw new ArgumentException("Unsupported device definition.");
        var copy = savedWire.ToArray();
        if (!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(copy), baseHash)) throw new InvalidDataException("Removal baseline hash mismatch.");
        var saved = SnapshotExport.DecodeFile(d, copy);
        _ = EditorCommand.ClearCell(d, saved.Image.Slot, row, column);
        if (savedScene is < 1 or > 8 || BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body) != savedScene - 1)
            throw new InvalidDataException("Caller-supplied saved scene does not match the retained body field.");
        var snapshot = PresetBody.Decode(d, saved.Image);
        if (effectId is < 1 or > 1000 || snapshot.BlockRecords.Count(r => r.Offset >= 12 &&
            BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body.AsSpan(r.Offset - 12, 2)) == effectId) != 1)
            throw new InvalidDataException("Removal requires one identifiable complete instance record.");
        var cell = snapshot.Grid.Single(c => c.Row == row && c.Column == column);
        if (cell.EffectId != effectId || snapshot.Grid.Count(c => c.EffectId == effectId) != 1 || snapshot.Limitations.Length != 0 ||
            !GridIsolation.IsIsolated(d, snapshot.Grid, row, column))
            throw new InvalidDataException("Removal requires one exact instance at an isolated cell with complete baseline semantics.");
        var plan = new BlockRemovalPlan(d, saved, row, column, effectId, savedScene);
        if (undoWire != null)
        {
            var original = SnapshotExport.DecodeFile(d, undoWire.ToArray());
            if (original.Image.Slot != plan.Slot) throw new InvalidDataException("Insertion undo target mismatch.");
            EditorPreservation.VerifyBlockRemoval(d, saved.Image, original.Image, row, column, effectId);
            plan.undo = original.Image;
        }
        return plan;
    }
    internal void VerifyBase(SavedCapture saved)
    {
        if (saved.Image.Slot != Slot || !StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(saved.Sysex), BaseSha256))
            throw new InvalidDataException("Saved removal baseline changed.");
    }
    internal void VerifyBase(PresetFileInspection live)
    {
        if (live.Model != Device.Model || !live.RawImage.SequenceEqual(baseline.RawImage) || !live.Body.SequenceEqual(baseline.Body))
            throw new InvalidDataException("Unsaved work or unexpected loaded contents; do not remove or reload.");
    }
    internal void VerifyChange(PresetFileInspection live) => VerifyChange(new StoredPresetImage(Slot, live.Name, live.SceneNames, live.RawImage, live.Body));
    internal void VerifyChange(StoredPresetImage changed)
    {
        EditorPreservation.VerifyBlockRemoval(Device, baseline, changed, Row, Column, EffectId);
        if (undo != null && (!undo.RawImage.SequenceEqual(changed.RawImage) || !undo.Body.SequenceEqual(changed.Body)))
            throw new InvalidDataException("Insertion undo differs from the complete original preset.");
    }
}

/// <summary>Closed removal scope: exact target/cell/instance; verified loading, one removal and one Save.</summary>
public sealed class BlockRemovalScope : EditorScope
{
    public BlockRemovalPlan Plan { get; }
    public override DeviceDefinition Device => Plan.Device;
    internal bool CanLoad, Prepared, CellSelected, BufferVerified, SavedVerified, Completed;
    private int claimed;
    public BlockRemovalScope(BlockRemovalPlan plan) => Plan = plan ?? throw new ArgumentNullException(nameof(plan));
    internal void ClaimSession()
    { if (Interlocked.Exchange(ref claimed, 1) != 0) throw new InvalidOperationException("Removal scope cannot be reused across sessions."); }
    public override void Validate(EditorCommand c)
    {
        bool permitted = c.Operation switch
        {
            EditorOperation.ReloadPreset => CanLoad || SavedVerified,
            EditorOperation.SelectCell => Prepared && c.Row == Plan.Row && c.Column == Plan.Column,
            EditorOperation.ClearCell => Prepared && CellSelected && c.Row == Plan.Row && c.Column == Plan.Column,
            EditorOperation.SavePreset => BufferVerified,
            _ => false
        };
        if (c.Device != Device || c.Slot != Plan.Slot || !permitted)
            throw new InvalidOperationException("Command is outside the verified isolated block-removal scope.");
    }
}

/// <summary>One persistent transport owner for a reviewed target list. Only a completed item can yield to the next scope.</summary>
public sealed class BlockRemovalBatchScope : EditorScope
{
    private readonly BlockRemovalScope[] scopes;
    private readonly HashSet<BlockRemovalScope> used = [];
    private BlockRemovalScope? active;
    public override DeviceDefinition Device { get; }
    public BlockRemovalBatchScope(IEnumerable<BlockRemovalScope> targets)
    {
        scopes = targets.ToArray();
        if (scopes.Length == 0 || scopes.Select(s=>s.Plan.Slot).Distinct().Count()!=scopes.Length || scopes.Any(s=>s.Device!=scopes[0].Device))
            throw new ArgumentException("Batch scope needs distinct targets from one model.");
        Device=scopes[0].Device;
    }
    public void Activate(BlockRemovalScope scope)
    {
        if (!scopes.Contains(scope) || used.Contains(scope) || active != null && !active.Completed)
            throw new InvalidOperationException("Only a verified completed removal can yield transport authorization; no scope replay.");
        used.Add(scope);active=scope;
    }
    public override void Validate(EditorCommand command) =>
        (active ?? throw new InvalidOperationException("No batch removal item activated.")).Validate(command);
}
