using System.Buffers.Binary;
namespace Fractal.Core;

/// <summary>One isolated, caller-resolved instance insertion, bound to a complete saved baseline.</summary>
public sealed class BlockInsertionPlan
{
    public DeviceDefinition Device { get; }
    public int Slot => baseline.Slot;
    public int Row { get; }
    public int Column { get; }
    public int EffectId { get; }
    public int SavedScene { get; }
    public string BaseSha256 { get; }
    private readonly StoredPresetImage baseline;
    private BlockInsertionPlan(DeviceDefinition d, SavedCapture saved, int row, int column, int id, int scene)
    { Device = d; baseline = saved.Image; Row = row; Column = column; EffectId = id; SavedScene = scene; BaseSha256 = PresetBody.Sha256(saved.Sysex); }
    public static BlockInsertionPlan Create(DeviceDefinition d, byte[] savedWire, string baseHash, int row, int column, int effectId, int savedScene)
    {
        if (d != DeviceDefinition.Create(d.Model, d.PresetCapacity)) throw new ArgumentException("Unsupported device definition.");
        var copy = savedWire.ToArray();
        if (!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(copy), baseHash)) throw new InvalidDataException("Insertion baseline hash mismatch.");
        var saved = SnapshotExport.DecodeFile(d, copy);
        _ = EditorCommand.InsertBlock(d, saved.Image.Slot, row, column, effectId);
        if (savedScene is < 1 or > 8 || BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body) != savedScene - 1)
            throw new InvalidDataException("Caller-supplied saved scene does not match the retained body field.");
        var snapshot = PresetBody.Decode(d, saved.Image);
        var cell = snapshot.Grid.Single(c => c.Row == row && c.Column == column);
        if (cell.EffectId != 0 || snapshot.Grid.Any(c => c.EffectId == effectId) || snapshot.Limitations.Length != 0 ||
            !GridIsolation.IsIsolated(d, snapshot.Grid, row, column))
            throw new InvalidDataException("Insertion requires an absent instance and an isolated empty cell with complete baseline semantics.");
        return new(d, saved, row, column, effectId, savedScene);
    }
    internal void VerifyBase(SavedCapture saved)
    {
        if (saved.Image.Slot != Slot || !StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(saved.Sysex), BaseSha256))
            throw new InvalidDataException("Saved insertion baseline changed.");
    }
    internal void VerifyBase(PresetFileInspection live)
    {
        if (live.Model != Device.Model || !live.RawImage.SequenceEqual(baseline.RawImage) || !live.Body.SequenceEqual(baseline.Body))
            throw new InvalidDataException("Unsaved work or unexpected loaded contents; do not insert or reload.");
    }
    internal void VerifyChange(PresetFileInspection live) => VerifyChange(new StoredPresetImage(Slot, live.Name, live.SceneNames, live.RawImage, live.Body));
    internal void VerifyChange(StoredPresetImage changed) =>
        EditorPreservation.VerifyBlockInsertion(Device, baseline, changed, Row, Column, EffectId);
}

/// <summary>Closed insertion scope: exact target/cell/instance; verified loading, one insertion and one Save.</summary>
public sealed class BlockInsertionScope : EditorScope
{
    public BlockInsertionPlan Plan { get; }
    public override DeviceDefinition Device => Plan.Device;
    internal bool CanLoad, Prepared, CellSelected, BufferVerified, SavedVerified, Completed;
    private int claimed;
    public BlockInsertionScope(BlockInsertionPlan plan) => Plan = plan ?? throw new ArgumentNullException(nameof(plan));
    internal void ClaimSession()
    { if (Interlocked.Exchange(ref claimed, 1) != 0) throw new InvalidOperationException("Insertion scope cannot be reused across sessions."); }
    public override void Validate(EditorCommand c)
    {
        bool permitted = c.Operation switch
        {
            EditorOperation.ReloadPreset => CanLoad || SavedVerified,
            EditorOperation.SelectCell => Prepared && c.Row == Plan.Row && c.Column == Plan.Column,
            EditorOperation.InsertBlock => Prepared && CellSelected && c.Row == Plan.Row && c.Column == Plan.Column && c.EffectId == Plan.EffectId,
            EditorOperation.SavePreset => BufferVerified,
            _ => false
        };
        if (c.Device != Device || c.Slot != Plan.Slot || !permitted)
            throw new InvalidOperationException("Command is outside the verified isolated block-insertion scope.");
    }
}

/// <summary>One persistent transport owner for a reviewed target list. Only a completed item can yield to the next scope.</summary>
public sealed class BlockInsertionBatchScope : EditorScope
{
    private readonly BlockInsertionScope[] scopes;
    private readonly HashSet<BlockInsertionScope> used = [];
    private BlockInsertionScope? active;
    public override DeviceDefinition Device { get; }
    public BlockInsertionBatchScope(IEnumerable<BlockInsertionScope> targets)
    {
        scopes = targets.ToArray();
        if (scopes.Length == 0 || scopes.Select(s=>s.Plan.Slot).Distinct().Count()!=scopes.Length || scopes.Any(s=>s.Device!=scopes[0].Device))
            throw new ArgumentException("Batch scope needs distinct targets from one model.");
        Device=scopes[0].Device;
    }
    public void Activate(BlockInsertionScope scope)
    {
        if (!scopes.Contains(scope) || used.Contains(scope) || active != null && !active.Completed)
            throw new InvalidOperationException("Only a verified completed insertion can yield transport authorization; no scope replay.");
        used.Add(scope);active=scope;
    }
    public override void Validate(EditorCommand command) =>
        (active ?? throw new InvalidOperationException("No batch insertion item activated.")).Validate(command);
}
