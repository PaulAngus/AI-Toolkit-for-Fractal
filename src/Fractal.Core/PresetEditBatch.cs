using System.Text.Json;
using System.Text.Json.Serialization;
namespace Fractal.Core;

public sealed record EditBatchReference(string Bundle, string Sha256);
public sealed record SavedPresetGuard(int Slot, string Sha256, string Capture);
public sealed record EditBatchRequest(int SchemaVersion, LiveControlContext Context, EditBatchReference[] Plans, bool DiscardChanges = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SavedPresetGuard[]? Guards = null);

/// <summary>One ordered, immutable batch; existing single-preset transactions remain the unit of verification.</summary>
public sealed class PresetEditBatch
{
    private readonly LiveControlContext initial;
    private readonly PresetEditPlan[] plans;
    private readonly PresetLoadScope?[] loads;
    private readonly PresetEditExecution[] executions;
    private readonly bool discard;
    private readonly SavedPresetGuard[]? guards;
    public PresetEditPlan[] Plans => plans.ToArray();
    private int attempted;
    public DeviceDefinition Device { get; }
    public string Firmware => initial.Firmware;
    public EditBatchScope Scope { get; }
    public PresetEditBatch(LiveControlContext context, IEnumerable<PresetEditPlan> inputs, bool discardChanges = false, SavedPresetGuard[]? savedGuards = null)
    {
        initial = JsonSerializer.Deserialize<LiveControlContext>(SnapshotExport.Json(context), LiveControlScope.JsonOptions)!;
        _ = new LiveControlScope(new(1, initial, "scene", Scene: initial.Current.Scene));
        Device = DeviceDefinition.Create(initial.Model, initial.Capacity); discard = discardChanges; plans = inputs.ToArray();
        guards = savedGuards == null ? null : JsonSerializer.Deserialize<SavedPresetGuard[]>(SnapshotExport.Json(savedGuards), LiveControlScope.JsonOptions)!;
        if (guards != null)
        {
            if (guards.Length is < 1 or > 64 || guards.Any(g => g == null) || guards.Select(g => g.Slot).Distinct().Count() != guards.Length) throw new InvalidDataException("Invalid batch source/destination guards.");
            foreach (var g in guards)
            {
                byte[] capture = Convert.FromBase64String(g.Capture); Device.ValidateSlot(g.Slot);
                if (PresetBody.Sha256(capture) != g.Sha256 || SnapshotExport.DecodeFile(Device, capture).Image.Slot != g.Slot ||
                    plans.Any(p => p.Slot == g.Slot && p.BaseSha256 != g.Sha256) ||
                    initial.Current.Slot == g.Slot && PresetBody.Sha256(Convert.FromBase64String(initial.SavedCapture)) != g.Sha256)
                    throw new InvalidDataException("Batch guard capture/slot/base differs.");
            }
        }
        if (plans.Length is < 1 or > 32 || plans.Select(p => p.Slot).Distinct().Count() != plans.Length ||
            plans.Any(p => p.Device != Device || p.Firmware != Firmware || !p.Executable || p.Persistence != "save"))
            throw new InvalidDataException("Batch needs 1–32 distinct executable saved-edit plans in one model/firmware context.");
        if (plans.Sum(p => Items(p).GetArrayLength()) > 256) throw new InvalidDataException("Batch exceeds 256 operations.");
        byte[] saved = Convert.FromBase64String(initial.SavedCapture), loaded = Convert.FromBase64String(initial.LoadedCapture);
        if (plans.Any(p => p.Slot == initial.Current.Slot && p.BaseSha256 != PresetBody.Sha256(saved))) throw new InvalidDataException("Initial capture and target baseline differ.");
        var current = initial.Current; loads = new PresetLoadScope?[plans.Length]; executions = plans.Select(p => new PresetEditExecution(p)).ToArray();
        var sequence = new List<EditorScope>(); bool first = true;
        for (int i = 0; i < plans.Length; i++)
        {
            var p = plans[i]; if (!p.Changed) continue;
            bool difference = !PresetFile.Inspect(Device, loaded).RawImage.SequenceEqual(SnapshotExport.DecodeFile(Device, saved).Image.RawImage);
            if (current.Slot != p.Slot || current.Scene != p.BeforeScene || difference)
            {
                loads[i] = new(Device, Firmware, current, saved, loaded, p.Baseline, first && discard);
                sequence.Add(loads[i]!);
            }
            sequence.Add(executions[i].Scope); first = false;
            saved = p.Expected; loaded = Temporary(saved); current = new(p.Slot, PresetFile.Inspect(Device, saved).Name, p.SavedScene);
        }
        Scope = new(Device, sequence);
    }
    private static JsonElement Items(PresetEditPlan p) { using var doc = JsonDocument.Parse(SnapshotExport.Json(p.Report())); return doc.RootElement.GetProperty("items").Clone(); }
    private static byte[] Temporary(byte[] saved) { var b = saved.ToArray(); b[6] = b[7] = 127; b[11] = SysexProtocol.ComputeChecksum(b.AsSpan(0, 11)); return b; }
    public object Report() => new { model = Device.Model, capacity = Device.PresetCapacity, firmware = Firmware, discard_changes = discard,
        initial_selection = initial.Current, ordered_targets = plans.Select(p => new { slot = p.Slot, changed = p.Changed, plan_sha256 = p.PlanSha256, plan = p.Report() }),
        saved_guards = guards?.Select(g => new { g.Slot, g.Sha256 }),
        automatic_retry = false, automatic_rollback = false, final_selection = "last changed target; unchanged targets are read without navigation", hardware_qualified = false, ports_opened = false };
    public void WriteBundle(string folder)
    {
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Choose a new batch directory.");
        Directory.CreateDirectory(folder); var refs = new List<EditBatchReference>();
        for (int i = 0; i < plans.Length; i++) { string name = $"item-{i:D3}"; plans[i].WriteBundle(Path.Combine(folder, name)); refs.Add(new(name, plans[i].PlanSha256)); }
        File.WriteAllText(Path.Combine(folder, "batch.json"), SnapshotExport.Json(new EditBatchRequest(1, initial, refs.ToArray(), discard, guards)));
    }
    public static PresetEditBatch Build(byte[] json, string relativeTo, bool contained = false)
    {
        if (json.Length > 5_000_000) throw new InvalidDataException("Batch request exceeds bound.");
        var request = JsonSerializer.Deserialize<EditBatchRequest>(json, LiveControlScope.JsonOptions) ?? throw new InvalidDataException("Missing batch request.");
        if (request.SchemaVersion != 1 || request.Plans == null || request.Plans.Length is < 1 or > 32) throw new InvalidDataException("Invalid batch request.");
        var plans = request.Plans.Select(r =>
        {
            if (contained && (r.Bundle != Path.GetFileName(r.Bundle) || r.Bundle is "." or "..")) throw new InvalidDataException("Batch plan must be a contained directory.");
            return PresetEditPlan.OpenBundle(Path.GetFullPath(r.Bundle, relativeTo), r.Sha256);
        });
        return new(request.Context, plans, request.DiscardChanges, request.Guards);
    }
    public static PresetEditBatch Open(string folder, string hash)
    {
        string file = Path.Combine(folder, "batch.json"); if (new FileInfo(file).Length > 5_000_000) throw new InvalidDataException("Batch exceeds bound.");
        byte[] bytes = File.ReadAllBytes(file); if (PresetBody.Sha256(bytes) != hash) throw new InvalidDataException("Batch hash mismatch.");
        return Build(bytes, Path.GetFullPath(folder), true);
    }
    public async Task<object> ReconcileAsync(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        if (context.Definition != Device || context.Firmware != Firmware) throw new InvalidDataException("Batch context mismatch.");
        var before = await session.CurrentAsync(context, timeout, token); var states = new List<object>();
        foreach (var p in plans)
        {
            var saved = await session.SavedAsync(context, p.Slot, timeout, token); string hash = PresetBody.Sha256(saved.Sysex);
            var expected = SnapshotExport.DecodeFile(Device, p.Expected);
            string state = hash == p.BaseSha256 ? "original" : hash == p.ExpectedSha256 ? "result-observed" :
                PresetUploadPlan.EquivalentCompression(expected.Image.RawImage, expected.Image.Body,
                    saved.Image.RawImage, saved.Image.Body) ? "result-equivalent" : "other";
            states.Add(new { slot = p.Slot, saved_sha256 = hash, state });
        }
        var guardStates = new List<object>();
        foreach (var g in guards ?? [])
        {
            string hash = PresetBody.Sha256((await session.SavedAsync(context, g.Slot, timeout, token)).Sysex);
            guardStates.Add(new { g.Slot, saved_sha256 = hash, original_unchanged = hash == g.Sha256, targeted = plans.Any(p => p.Slot == g.Slot) });
        }
        var live = await session.EditBufferAsync(context, timeout, token);
        var current = await session.SavedAsync(context, before.Slot, timeout, token);
        if (before != await session.CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection drift during reconciliation.");
        return new { complete = true, mode = "query-only", states, guard_states = guardStates, current = before, loaded_sha256 = PresetBody.Sha256(live.Sysex),
            unsaved_changes = !live.Image.RawImage.SequenceEqual(current.Image.RawImage), automatic_retry = false,
            next_action = "Retain completed receipts. Prepare a fresh batch with a fresh context and only explicitly selected original-state targets; never replay an uncertain upload or Save." };
    }
    private async Task VerifyGuardsAsync(ReadSession reader, DeviceContext context, Dictionary<int, string> verified, TimeSpan timeout, CancellationToken token)
    {
        if (guards == null) return;
        var before = await reader.CurrentAsync(context, timeout, token);
        foreach (var g in guards)
            if (PresetBody.Sha256((await reader.SavedAsync(context, g.Slot, timeout, token)).Sysex) != verified.GetValueOrDefault(g.Slot, g.Sha256))
                throw new InvalidDataException("A guarded organization source/destination changed; no further mutation.");
        if (before != await reader.CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection changed while checking guarded captures.");
    }
    public async Task<object> RunAsync(IPresetRestoreTransport transport, DeviceContext context, string destination, TimeSpan timeout, CancellationToken token, TimeSpan? spacing = null)
    {
        if (context.Definition != Device || context.Firmware != Firmware) throw new InvalidDataException("Batch context mismatch.");
        if (Interlocked.Exchange(ref attempted, 1) != 0) throw new InvalidOperationException("Batch already attempted; reconcile without replay.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new execution directory.");
        Directory.CreateDirectory(destination); WriteBundle(Path.Combine(destination, "batch")); var completed = new List<object>();
        string batchHash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(destination, "batch/batch.json")));
        var verifiedHashes = new Dictionary<int, string>();
        int index = -1; int? lastChangedSlot = null, lastChangedScene = null; string phase = "preflight";
        void Checkpoint(string status, string? failure = null)
        {
            string file = Path.Combine(destination, "progress.json");
            PresetInventory.AtomicWrite(file, System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(new { schema_version = 1, protocol_release = ProtocolRelease.Version,
                batch_sha256 = batchHash,
                status, phase, active_index = index, completed, failure, automatic_retry = false, automatic_rollback = false })), true);
        }
        Checkpoint("started");
        try
        {
            await using (var reader = new ReadSession(new BorrowedPresetTransport(transport)))
            {
                if (SnapshotExport.Json(await LiveControlScope.CaptureAsync(reader, context, timeout, token)) != SnapshotExport.Json(initial)) throw new InvalidDataException("Initial batch context changed.");
                foreach (var p in plans) if (PresetBody.Sha256((await reader.SavedAsync(context, p.Slot, timeout, token)).Sysex) != p.BaseSha256) throw new InvalidDataException("A batch target changed before the first mutation.");
                await VerifyGuardsAsync(reader, context, verifiedHashes, timeout, token);
            }
            for (index = 0; index < plans.Length; index++)
            {
                var p = plans[index]; phase = "verify-target"; Checkpoint("started");
                await using (var reader = new ReadSession(new BorrowedPresetTransport(transport)))
                    await VerifyGuardsAsync(reader, context, verifiedHashes, timeout, token);
                if (!p.Changed)
                {
                    await using var reader = new ReadSession(new BorrowedPresetTransport(transport));
                    if (PresetBody.Sha256((await reader.SavedAsync(context, p.Slot, timeout, token)).Sysex) != p.BaseSha256) throw new InvalidDataException("Unchanged target has changed.");
                    verifiedHashes[p.Slot] = p.BaseSha256;
                    completed.Add(new { slot = p.Slot, unchanged = true, complete = true }); Checkpoint("running"); continue;
                }
                if (loads[index] is PresetLoadScope load)
                {
                    phase = "load-target"; Checkpoint("attempting");
                    await using var reader = new ReadSession(new BorrowedPresetTransport(transport));
                    var current = await reader.CurrentAsync(context, timeout, token);
                    var currentSaved = await reader.SavedAsync(context, current.Slot, timeout, token);
                    var currentLoaded = await reader.EditBufferAsync(context, timeout, token);
                    var targetSaved = await reader.SavedAsync(context, p.Slot, timeout, token);
                    if (lastChangedSlot.HasValue && (current.Slot != lastChangedSlot || current.Scene != lastChangedScene))
                        throw new InvalidDataException("Batch selection changed between completed items.");
                    string currentHash = verifiedHashes.GetValueOrDefault(current.Slot) ??
                        (current.Slot == initial.Current.Slot ? PresetBody.Sha256(Convert.FromBase64String(initial.SavedCapture)) : "");
                    if (PresetBody.Sha256(currentSaved.Sysex) != currentHash ||
                        PresetBody.Sha256(targetSaved.Sysex) != p.BaseSha256)
                        throw new InvalidDataException("Batch current or target saved state changed before load.");
                    bool firstChanged = index == Array.FindIndex(plans, item => item.Changed);
                    if (firstChanged && (current != initial.Current ||
                        !currentSaved.Sysex.SequenceEqual(Convert.FromBase64String(initial.SavedCapture)) ||
                        !currentLoaded.Sysex.SequenceEqual(Convert.FromBase64String(initial.LoadedCapture))))
                        throw new InvalidDataException("Initial reviewed batch context changed before load.");
                    var fresh = new PresetLoadScope(Device, Firmware, current, currentSaved.Sysex, currentLoaded.Sysex,
                        targetSaved.Sysex, firstChanged && discard);
                    Scope.ReplacePending(load, fresh); loads[index] = fresh; load = fresh;
                    Scope.Activate(load); reader.AuthorizePresetLoad(load);
                    await reader.PreparePresetLoadAsync(context, timeout, token);
                    foreach (var command in load.Commands) await reader.SendEditorAsync(command, timeout, token);
                    await reader.VerifyPresetLoadAsync(context, timeout, token); Scope.Complete(load);
                }
                phase = "edit-target"; Checkpoint("attempting"); var execution = executions[index]; Scope.Activate(execution.Scope);
                await using (var reader = new ReadSession(new BorrowedPresetTransport(transport)))
                {
                    await VerifyGuardsAsync(reader, context, verifiedHashes, timeout, token);
                    var result = await execution.RunAsync(reader, context, Path.Combine(destination, $"item-{index:D3}"), timeout, token, spacing);
                    if (!result.Complete || result.ObservedResultSha256 == null) throw new InvalidDataException("Incomplete edit transaction.");
                    verifiedHashes[p.Slot] = result.ObservedResultSha256;
                    lastChangedSlot = p.Slot; lastChangedScene = p.SavedScene;
                    completed.Add(result);
                }
                Scope.Complete(execution.Scope); Checkpoint("running");
            }
            phase = "verify-all-targets"; Checkpoint("running");
            await using (var reader = new ReadSession(new BorrowedPresetTransport(transport)))
            {
                foreach (var p in plans)
                    if (PresetBody.Sha256((await reader.SavedAsync(context, p.Slot, timeout, token)).Sysex) != verifiedHashes[p.Slot])
                        throw new InvalidDataException("A completed batch target changed before final verification.");
                await VerifyGuardsAsync(reader, context, verifiedHashes, timeout, token);
                if (plans.All(p => p.Slot != initial.Current.Slot) && PresetBody.Sha256((await reader.SavedAsync(context, initial.Current.Slot, timeout, token)).Sysex) != PresetBody.Sha256(Convert.FromBase64String(initial.SavedCapture)))
                    throw new InvalidDataException("Untargeted initial saved preset changed.");
                if (lastChangedSlot.HasValue)
                {
                    var selected = await reader.CurrentAsync(context, timeout, token);
                    var saved = await reader.SavedAsync(context, lastChangedSlot.Value, timeout, token);
                    var loaded = await reader.EditBufferAsync(context, timeout, token);
                    if (selected.Slot != lastChangedSlot || selected.Scene != lastChangedScene || selected.Name != saved.Image.Name ||
                        PresetBody.Sha256(saved.Sysex) != verifiedHashes[lastChangedSlot.Value] ||
                        !PresetUploadPlan.EquivalentCompression(saved.Image.RawImage, saved.Image.Body, loaded.Image.RawImage, loaded.Image.Body) ||
                        selected != await reader.CurrentAsync(context, timeout, token))
                        throw new InvalidDataException("Final selection or complete saved/loaded result changed.");
                }
                else if (SnapshotExport.Json(await LiveControlScope.CaptureAsync(reader, context, timeout, token)) != SnapshotExport.Json(initial))
                    throw new InvalidDataException("No-op batch current context changed.");
            }
            phase = "verified-complete"; Checkpoint("complete"); return new { complete = true, completed_count = completed.Count, results = completed, hardware_qualified = false };
        }
        catch (Exception e) { Checkpoint("stopped-uncertain", e.Message); throw; }
    }
}
public sealed class EditBatchScope : EditorScope
{
    public override DeviceDefinition Device { get; }
    private readonly EditorScope[] sequence;
    private int next; private EditorScope? active;
    internal EditBatchScope(DeviceDefinition d, IEnumerable<EditorScope> scopes) { Device = d; sequence = scopes.ToArray(); }
    internal void ReplacePending(EditorScope previous, EditorScope replacement)
    {
        if (active != null || next >= sequence.Length || !ReferenceEquals(sequence[next], previous) || replacement.Device != Device)
            throw new InvalidOperationException("Invalid pending batch load replacement.");
        sequence[next] = replacement;
    }
    internal void Activate(EditorScope scope) { if (active != null || next >= sequence.Length || !ReferenceEquals(sequence[next], scope)) throw new InvalidOperationException("Invalid batch transition."); active = scope; }
    internal void Complete(EditorScope scope)
    {
        if (!ReferenceEquals(active, scope) || scope is PresetLoadScope load && !load.Completed || scope is PresetRestoreScope restore && !restore.Completed) throw new InvalidOperationException("Unverified batch transition.");
        active = null; next++;
    }
    public override void Validate(EditorCommand command) => (active ?? throw new InvalidOperationException("No active batch step.")).Validate(command);
    public void ValidateUpload(PresetUploadPlan plan, int index)
    { if (active is not PresetRestoreScope restore) throw new InvalidOperationException("No active batch upload."); restore.ValidateUpload(plan, index); }
}
public sealed class BorrowedPresetTransport(IPresetRestoreTransport inner) : IPresetRestoreTransport
{
    public event Action<byte[]>? Received { add => inner.Received += value; remove => inner.Received -= value; }
    public event Action<Exception>? Faulted { add => inner.Faulted += value; remove => inner.Faulted -= value; }
    public Task SendReadAsync(byte[] bytes, CancellationToken token) => inner.SendReadAsync(bytes, token);
    public Task SendEditorAsync(EditorCommand command, CancellationToken token) => inner.SendEditorAsync(command, token);
    public Task SendPresetFrameAsync(PresetUploadPlan plan, int index, CancellationToken token) => inner.SendPresetFrameAsync(plan, index, token);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
