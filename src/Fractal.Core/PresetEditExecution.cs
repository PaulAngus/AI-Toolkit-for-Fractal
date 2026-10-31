using System.Text;
using System.Text.Json;

namespace Fractal.Core;

public sealed record PresetEditExecutionResult(bool Complete, string Status, string PlanSha256, string BaseSha256,
    string ExpectedSha256, string Persistence, int Slot, bool SavedUnchanged, bool HardwareQualified = false,
    string? ObservedResultSha256 = null);

/// <summary>One reviewed complete-image transaction using the existing upload/save/reload scope.</summary>
public sealed class PresetEditExecution
{
    private readonly PresetEditPlan plan;
    private readonly bool undo;
    private readonly string? undoBaseHash;
    private string BaseHash => undo ? undoBaseHash! : plan.BaseSha256;
    private string ResultHash => undo ? plan.BaseSha256 : plan.ExpectedSha256;
    private int attempted;
    public PresetRestoreScope Scope { get; }
    public PresetEditExecution(PresetEditPlan plan) : this(plan, false, null) { }
    private PresetEditExecution(PresetEditPlan plan, bool undo, string? undoBaseHash)
    {
        if (!plan.Executable) throw new InvalidOperationException("Edit plan has conflicts.");
        this.plan = plan; this.undo = undo; this.undoBaseHash = undoBaseHash;
        Scope = new(PresetUploadPlan.CreateForEdit(plan.Device, plan.Slot, undo ? plan.BeforeScene : plan.SavedScene, undo ? plan.Baseline : plan.Expected,
            ResultHash, undo ? plan.SavedScene : plan.BeforeScene), plan.Persistence == "save");
    }
    public static (PresetEditPlan Plan, PresetEditExecution Execution) PrepareUndo(string receiptDirectory, string receiptSha256)
    {
        string path = Path.Combine(receiptDirectory, "attempt.json");
        if (new FileInfo(path).Length > 65536 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid edit receipt.");
        byte[] bytes = File.ReadAllBytes(path);
        if (PresetBody.Sha256(bytes) != receiptSha256) throw new InvalidDataException("Edit receipt hash changed.");
        using var doc = JsonDocument.Parse(bytes); var receipt = doc.RootElement;
        if (receipt.GetProperty("status").GetString() != "complete" || receipt.GetProperty("phase").GetString() != "verified-saved" ||
            receipt.GetProperty("mode").GetString() != "apply") throw new InvalidDataException("Undo requires a completed saved edit receipt.");
        var plan = PresetEditPlan.OpenBundle(Path.Combine(receiptDirectory, "plan"), receipt.GetProperty("plan_sha256").GetString()!, allowPriorReceiptRelease: true);
        string resultPath = Path.Combine(receiptDirectory, "verified-reloaded.syx");
        string savedPath = Path.Combine(receiptDirectory, "verified-saved.syx");
        if (new FileInfo(resultPath).Length > 60000 || new FileInfo(savedPath).Length > 60000)
            throw new InvalidDataException("Invalid verified edit capture.");
        byte[] observed = File.ReadAllBytes(resultPath);
        string observedHash = PresetBody.Sha256(observed);
        string recordedHash = receipt.TryGetProperty("observed_result_sha256", out var hashProperty) && hashProperty.ValueKind == JsonValueKind.String
            ? hashProperty.GetString()! : plan.ExpectedSha256;
        var expectedImage = PresetFile.Inspect(plan.Device, plan.Expected);
        var observedImage = SnapshotExport.DecodeFile(plan.Device, observed);
        if (observedHash != recordedHash || !observed.SequenceEqual(File.ReadAllBytes(savedPath)) ||
            observedImage.Image.Slot != plan.Slot ||
            !PresetUploadPlan.EquivalentCompression(expectedImage.RawImage, expectedImage.Body, observedImage.Image.RawImage, observedImage.Image.Body) ||
            receipt.GetProperty("base_sha256").GetString() != plan.BaseSha256 || receipt.GetProperty("expected_sha256").GetString() != plan.ExpectedSha256)
            throw new InvalidDataException("Verified edit result does not match its immutable plan.");
        return (plan, new(plan, true, observedHash));
    }
    public async Task<object> ReconcileAsync(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token, string? destination = null)
    {
        if (context.Definition != plan.Device || context.Firmware != plan.Firmware) throw new InvalidDataException("Reconciliation context mismatch.");
        var before = await session.CurrentAsync(context, timeout, token);
        if (before.Slot != plan.Slot || before.Scene != plan.SavedScene) throw new InvalidDataException("Select the reviewed preset/default scene before reconciliation.");
        var saved = await session.SavedAsync(context, plan.Slot, timeout, token);
        var loaded = await session.EditBufferAsync(context, timeout, token);
        var repeat = await session.SavedAsync(context, plan.Slot, timeout, token);
        if (!saved.Sysex.SequenceEqual(repeat.Sysex) || before != await session.CurrentAsync(context, timeout, token)) throw new InvalidDataException("State changed during reconciliation.");
        var original = SnapshotExport.DecodeFile(plan.Device, plan.Baseline);
        var result = SnapshotExport.DecodeFile(plan.Device, plan.Expected);
        string Match(byte[] raw, byte[] body) => raw.SequenceEqual(original.Image.RawImage) && body.SequenceEqual(original.Image.Body) ? "original" :
            raw.SequenceEqual(result.Image.RawImage) && body.SequenceEqual(result.Image.Body) ? "result" :
            PresetUploadPlan.EquivalentCompression(result.Image.RawImage, result.Image.Body, raw, body) ? "result-equivalent" : "other";
        string savedState = Match(saved.Image.RawImage, saved.Image.Body), liveState = Match(loaded.Image.RawImage, loaded.Image.Body);
        if (destination != null)
        {
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new reconciliation directory.");
            Directory.CreateDirectory(destination);
            plan.WriteBundle(Path.Combine(destination, "plan"));
            File.WriteAllBytes(Path.Combine(destination, "observed-saved.syx"), saved.Sysex);
            File.WriteAllBytes(Path.Combine(destination, "observed-loaded.syx"), loaded.Sysex);
        }
        return new { complete = true, mode = "query-only-reconciliation", plan_sha256 = plan.PlanSha256, saved = savedState, loaded = liveState,
            saved_sha256 = PresetBody.Sha256(saved.Sysex), loaded_sha256 = PresetBody.Sha256(loaded.Sysex),
            saved_loaded_equal = saved.Image.RawImage.SequenceEqual(loaded.Image.RawImage), automatic_retry = false,
            next_action = savedState == "result" && liveState == "result" ? "Result observed; no upload or Save replay. Retain receipt evidence for explicit undo." :
                savedState == "original" && liveState == "original" ? "Original observed; a newly reviewed apply may be prepared." :
                "Mixed or later state; retain both images and prepare a new explicit recovery action. No automatic mutation." };
    }
    public async Task<PresetEditExecutionResult> RunAsync(ReadSession session, DeviceContext context, string destination,
        TimeSpan timeout, CancellationToken token, TimeSpan? frameSpacing = null)
    {
        if (context.Definition != plan.Device || context.Firmware != plan.Firmware) throw new InvalidDataException("Edit device context differs from the reviewed plan.");
        if (Interlocked.Exchange(ref attempted, 1) != 0) throw new InvalidOperationException("Edit execution already attempted; inspect retained evidence before a fresh review.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new private edit evidence directory.");
        Directory.CreateDirectory(destination);
        plan.WriteBundle(Path.Combine(destination, "plan"));
        string phase = "preflight";
        string? observedResultHash = null;
        void Checkpoint(string status, string? error = null)
        {
            string path = Path.Combine(destination, "attempt.json");
            var record = new { schema_version = 1, protocol_release = ProtocolRelease.Version, model = plan.Device.Model,
                firmware = plan.Firmware, capacity = plan.Device.PresetCapacity, slot = plan.Slot, plan_sha256 = plan.PlanSha256,
                base_sha256 = BaseHash, expected_sha256 = ResultHash, observed_result_sha256 = observedResultHash,
                persistence = plan.Persistence, mode = undo ? "undo" : "apply",
                phase, status, error, updated_utc = DateTimeOffset.UtcNow, automatic_retry = false, automatic_rollback = false };
            PresetInventory.AtomicWrite(path, Encoding.UTF8.GetBytes(SnapshotExport.Json(record)), true);
        }
        Checkpoint("started");
        try
        {
            session.AuthorizePresetRestore(Scope);
            var before = await session.PreparePresetRestoreAsync(context, BaseHash, timeout, token);
            File.WriteAllBytes(Path.Combine(destination, "fresh-base.syx"), before.Sysex);
            if (!plan.Changed)
            {
                phase = "verified-unchanged"; Checkpoint("complete");
                return new(true, phase, plan.PlanSha256, BaseHash, ResultHash, plan.Persistence, plan.Slot, true);
            }
            phase = "upload"; Checkpoint("attempting");
            await session.UploadPresetAsync(TimeSpan.FromSeconds(30), frameSpacing ?? TimeSpan.FromMilliseconds(100), token);
            phase = "verify-live"; Checkpoint("started");
            var live = await session.VerifyUploadedPresetAsync(context, timeout, token);
            await plan.VerifyLiveNamesAsync(session, context, timeout, token, undo);
            File.WriteAllBytes(Path.Combine(destination, "verified-live.syx"), live.Sysex);
            var saved = await session.SavedAsync(context, plan.Slot, timeout, token);
            if (PresetBody.Sha256(saved.Sysex) != BaseHash) throw new InvalidDataException("Temporary upload changed saved content.");
            if (plan.Persistence == "edit-buffer")
            {
                phase = "verified-live"; Checkpoint("complete");
                return new(true, phase, plan.PlanSha256, BaseHash, ResultHash, plan.Persistence, plan.Slot, true);
            }
            phase = "save"; Checkpoint("attempting");
            await session.SendEditorAsync(EditorCommand.SavePreset(plan.Device, plan.Slot), timeout, token);
            phase = "verify-saved"; Checkpoint("started");
            var first = await session.VerifyRestoredSavedAsync(context, timeout, token);
            var repeat = await session.VerifyRestoredSavedAsync(context, timeout, token);
            if (!first.Sysex.SequenceEqual(repeat.Sysex)) throw new InvalidDataException("Repeated saved images differ.");
            File.WriteAllBytes(Path.Combine(destination, "verified-saved.syx"), first.Sysex);
            phase = "reload"; Checkpoint("attempting");
            await session.SendEditorAsync(EditorCommand.ReloadPreset(plan.Device, plan.Slot), timeout, token);
            await session.VerifyUploadedPresetAsync(context, timeout, token);
            await plan.VerifyLiveNamesAsync(session, context, timeout, token, undo);
            var reloaded = await session.VerifyRestoredSavedAsync(context, timeout, token);
            if (!reloaded.Sysex.SequenceEqual(first.Sysex)) throw new InvalidDataException("Saved image changed after reload.");
            File.WriteAllBytes(Path.Combine(destination, "verified-reloaded.syx"), reloaded.Sysex);
            observedResultHash = PresetBody.Sha256(reloaded.Sysex);
            phase = "verified-saved"; Checkpoint("complete");
            return new(true, phase, plan.PlanSha256, BaseHash, ResultHash, plan.Persistence, plan.Slot, false,
                ObservedResultSha256: observedResultHash);
        }
        catch (Exception e) { Checkpoint("stopped-uncertain", e.Message); throw; }
    }
}
