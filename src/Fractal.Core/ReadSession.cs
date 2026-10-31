using System.Threading.Channels;
namespace Fractal.Core;

public interface IMidiTransport : IAsyncDisposable
{
    event Action<byte[]>? Received;
    event Action<Exception>? Faulted;
    Task SendReadAsync(byte[] request, CancellationToken token);
}
public sealed record DeviceContext(DeviceDefinition Definition, string Firmware, string CapacityEvidence);
public sealed record CurrentState(int Slot, string Name, int Scene);
public sealed record SavedCapture(StoredPresetImage Image, byte[] Sysex);
public sealed record ReadResult(DeviceContext Device, CurrentState Before, CurrentState After, SavedCapture[] Captures)
{
    public bool StatePreserved => Before == After;
}

/// <summary>One request at a time. Failed/ambiguous exchanges permanently poison this session.</summary>
public sealed partial class ReadSession : IAsyncDisposable
{
    private readonly IMidiTransport transport;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SysexAssembler assembler = new();
    private readonly Channel<byte[]> replies = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private Exception? fault;
    private bool disposed;
    private bool active;
    private EditorScope? pilotScope;
    private bool saveAttempted;
    private bool uploadAttempted, uploadComplete, restoreReloadAttempted;
    private bool uploadMonitoring;
    private DeviceContext? preparedRestoreContext;
    private string? preparedRestoreBaseHash;
    private readonly object receiveLock = new();
    public ReadSession(IMidiTransport transport) { this.transport = transport; transport.Received += Receive; transport.Faulted += Fail; }
    private void Fail(Exception error) { lock (receiveLock) { fault ??= error; replies.Writer.TryComplete(error); } }
    private void Receive(byte[] bytes)
    {
        lock (receiveLock)
        {
            if (disposed || fault != null || !active && pilotScope == null) return;
            try
            {
                foreach (byte[] frame in assembler.Feed(bytes))
                {
                    // Published checksumless pushes cannot satisfy a read. Keep them out of the reply queue;
                    // observers may subscribe to the same coordinated transport independently.
                    if (DeviceNotifications.IsPublishedPush(frame)) continue;
                    if (pilotScope != null && frame.Length is >= 10 and <= 12 && frame[4] == pilotScope.Device.ModelByte &&
                        frame[5] == 0x64 && (frame[6] is 1 or 0x0c || pilotScope is LiveControlScope && frame[6] is 10 or 11 || pilotScope is PerformanceControlScope && frame[6] is 0x0f or 0x10 or 0x11 or 0x14 || pilotScope is PresetRestoreScope && frame[6] is 0x77 or 0x78 or 0x79))
                    {
                        if (!SysexProtocol.ValidFrame(frame, frame[4], 0x64) || frame[7] != 0)
                            throw new IOException($"Device rejected/failed an editor operation (result {frame[7]}). Reconcile by readback; no replay.");
                    }
                    // Upload has no reply consumer: inspect rejection replies above without queuing
                    // successful acknowledgements or unsolicited parameter notifications.
                    if (active && !uploadMonitoring && !replies.Writer.TryWrite(frame)) throw new IOException("MIDI receive queue overflow; reconnect before retrying.");
                }
            }
            catch (Exception error) { Fail(error); }
        }
    }
    private async Task<T> Exchange<T>(byte[] request, Func<byte[], (bool Done, T Value)> accept,
        TimeSpan timeout, CancellationToken token, EditorCommand? scopedQuery = null)
    {
        if (scopedQuery == null)
        { if (!SysexProtocol.IsReadRequest(request)) throw new InvalidOperationException("Request is outside read-only allowlist."); }
        else
        {
            if (pilotScope is not PerformanceControlScope performance || scopedQuery.Operation != EditorOperation.QueryTempo || !scopedQuery.Frame.SequenceEqual(request))
                throw new InvalidOperationException("Tempo query requires an exact reviewed performance scope.");
            performance.Validate(scopedQuery);
        }
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        await gate.WaitAsync(token);
        try
        {
            lock (receiveLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (fault != null) throw new IOException("Session is unusable; reconnect before another request.", fault);
                while (replies.Reader.TryRead(out _)) { }
                assembler.Reset(); active = true;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            try
            {
                if (scopedQuery == null) await transport.SendReadAsync(request, deadline.Token);
                else
                {
                    ((PerformanceControlScope)pilotScope!).ConsumeQuery();
                    await ((IEditorTransport)transport).SendEditorAsync(scopedQuery, deadline.Token);
                }
                int frames = 0;
                while (true)
                {
                    byte[] frame = await replies.Reader.ReadAsync(deadline.Token);
                    lock (receiveLock) { if (fault != null) throw new IOException("MIDI receive failed; reconnect.", fault); }
                    if (++frames > 256) throw new IOException("Too many unrelated replies in one exchange.");
                    var result = accept(frame);
                    if (result.Done) return result.Value;
                }
            }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested)
            { Fail(error); throw new TimeoutException("MIDI read timed out. Reconnect; no automatic replay was sent.", error); }
            catch (Exception error) { Fail(error); throw; }
            finally { lock (receiveLock) { active = false; assembler.Reset(); } }
        }
        finally { gate.Release(); }
    }
    public async Task<DeviceContext> IdentifyAsync(string? expectedModel, int? capacity, TimeSpan timeout, CancellationToken token)
    {
        byte model = await Exchange(SysexProtocol.IdentityQuery(), frame =>
        {
            bool ok = frame.Length == 10 && frame[4] is 0x10 or 0x11 or 0x12 &&
                SysexProtocol.ValidFrame(frame, frame[4], 0x64, 10) && frame[6] == 0 && frame[7] == 0;
            return (ok, ok ? frame[4] : (byte)0);
        }, timeout, token);
        string name = DeviceDefinition.ModelName(model);
        if (expectedModel != null && expectedModel != name) throw new InvalidDataException("Identified device does not match requested model.");
        DeviceDefinition d = DeviceDefinition.Create(name, capacity);
        string firmware = await Exchange(SysexProtocol.FirmwareQuery(d), frame =>
        {
            bool ok = frame.Length >= 10 && SysexProtocol.ValidFrame(frame, d.ModelByte, 8);
            return (ok, ok ? FormattableString.Invariant($"{frame[6]}.{frame[7]:00}") : "");
        }, timeout, token);
        return new(d, firmware, name == "Axe-Fx III" ? "explicit-user-capacity" : "model-definition");
    }
    public async Task<CurrentState> CurrentAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        DeviceDefinition d = context.Definition;
        byte[] preset = await Exchange(SysexProtocol.PresetNameQuery(d), frame =>
            (SysexProtocol.ValidFrame(frame, d.ModelByte, 0x0d, 42) && SysexProtocol.PresetAddress(frame) < d.PresetCapacity, frame), timeout, token);
        int scene = await Exchange(SysexProtocol.CurrentSceneQuery(d), frame =>
            (SysexProtocol.ValidFrame(frame, d.ModelByte, 0x0c, 9) && frame[6] < 8, frame.Length > 6 ? frame[6] + 1 : 0), timeout, token);
        return new(SysexProtocol.PresetAddress(preset), SysexProtocol.ReadName(preset.AsSpan(8, 32)), scene);
    }
    public Task<string> PresetNameAsync(DeviceContext context, int slot, TimeSpan timeout, CancellationToken token) =>
        Exchange(SysexProtocol.PresetNameQuery(context.Definition, slot), frame =>
        {
            bool ok = SysexProtocol.ValidFrame(frame, context.Definition.ModelByte, 0x0d, 42) && SysexProtocol.PresetAddress(frame) == slot;
            return (ok, ok ? SysexProtocol.ReadName(frame.AsSpan(8, 32)) : "");
        }, timeout, token);
    public Task<string> SceneNameAsync(DeviceContext context, int scene, TimeSpan timeout, CancellationToken token) =>
        Exchange(SysexProtocol.SceneNameQuery(context.Definition, scene), frame =>
        {
            bool ok = SysexProtocol.ValidFrame(frame, context.Definition.ModelByte, 0x0e, 41) && frame[6] == scene - 1;
            return (ok, ok ? SysexProtocol.ReadName(frame.AsSpan(7, 32)) : "");
        }, timeout, token);
    public Task<SavedCapture> SavedAsync(DeviceContext context, int slot, TimeSpan timeout, CancellationToken token)
    {
        DeviceDefinition d = context.Definition;
        var decoder = new StoredPresetDecoder(d);
        List<byte> capture = [];
        bool started = false;
        return Exchange(SysexProtocol.SavedQuery(d, slot), frame =>
        {
            if (frame.Length < 6 || frame[4] != d.ModelByte || frame[5] is not (0x77 or 0x78 or 0x79)) return (false, null!);
            if (frame[5] == 0x77)
            {
                if (!SysexProtocol.ValidFrame(frame, d.ModelByte, 0x77, 13)) throw new InvalidDataException("Invalid saved dump header.");
                if ((frame[6] << 7 | frame[7]) != slot) return (false, null!);
                if (started) throw new InvalidDataException("Repeated saved dump header.");
                started = true;
            }
            if (!started) return (false, null!);
            capture.AddRange(frame);
            StoredPresetImage? image = decoder.AcceptImage(frame, slot);
            return (image != null, image != null ? new SavedCapture(image, capture.ToArray()) : null!);
        }, timeout, token);
    }
    public Task<SceneMidiCapture> SceneMidiAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var decoder = new SceneMidiTableDecoder(context.Definition);
        return Exchange(SysexProtocol.SceneMidiQuery(context.Definition), frame =>
        {
            var table = decoder.Accept(frame);
            return (table != null, table!);
        }, timeout, token);
    }
    /// <summary>Read the current edit buffer with strict payload integrity. Never label it as a saved-slot capture.</summary>
    public Task<EditBufferCapture> EditBufferAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        DeviceDefinition d = context.Definition;
        List<byte> bytes = [];
        int count = 0;
        return Exchange(SysexProtocol.EditBufferQuery(d), frame =>
        {
            if (frame.Length < 6 || frame[4] != d.ModelByte || frame[5] is not (0x77 or 0x78 or 0x79))
                return (false, null!);
            byte fn = count == 0 ? (byte)0x77 : count <= d.DumpChunks ? (byte)0x78 : (byte)0x79;
            int length = fn == 0x77 ? 13 : fn == 0x78 ? 3082 : 11;
            if (count > d.DumpChunks + 1 || !SysexProtocol.ValidFrame(frame, d.ModelByte, fn, length))
                throw new InvalidDataException("Unexpected edit-buffer transfer order/length/checksum.");
            bytes.AddRange(frame); count++;
            if (fn != 0x79) return (false, null!);
            byte[] wire = bytes.ToArray();
            return (true, new EditBufferCapture(PresetFile.Inspect(d, wire), wire));
        }, timeout, token);
    }
    public Task<BlockStatus[]> BlockStatusAsync(DeviceContext context, TimeSpan timeout, CancellationToken token) =>
        Exchange(SysexProtocol.BlockStatusQuery(context.Definition), frame =>
        {
            if (frame.Length < 6 || frame[4] != context.Definition.ModelByte || frame[5] != 0x13) return (false, Array.Empty<BlockStatus>());
            return (true, BlockStatusTable.Decode(context.Definition, frame));
        }, timeout, token);

    /// <summary>Enable only after verifying an empty saved target, backup and live selection. No generic restore is implied.</summary>
    public void AuthorizeEmptyPresetPilot(EmptyPresetPilotScope scope)
        => AuthorizeEditor(scope);

    /// <summary>Enable after checking the backup, saved base and selected target. One save attempt; full readback required.</summary>
    public void AuthorizePresetSave(PresetSaveScope scope) => AuthorizeEditor(scope);
    public void AuthorizePresetRestore(PresetRestoreScope scope)
    {
        if (transport is not IPresetRestoreTransport) throw new InvalidOperationException("Transport cannot upload preset frames.");
        AuthorizeEditor(scope);
    }

    private PresetRestoreScope RestoreScope => pilotScope as PresetRestoreScope ??
        throw new InvalidOperationException("No single-preset restore was authorized.");
    private void CheckRestoreState(DeviceContext context, CurrentState state)
    {
        var plan = RestoreScope.Plan;
        if (context.Definition != plan.Device || state.Slot != plan.Slot || state.Scene != (uploadComplete ? plan.ExpectedScene : plan.BeforeScene))
            throw new InvalidDataException("Restore requires its exact model, selected target and expected scene.");
    }
    /// <summary>Require a fresh saved base hash and an identical current buffer; reject unknown unsaved work.</summary>
    public async Task<SavedCapture> PreparePresetRestoreAsync(DeviceContext context, string savedBaseHash, TimeSpan timeout, CancellationToken token)
    {
        if (uploadAttempted) throw new InvalidOperationException("Cannot prepare a restore after upload was attempted.");
        preparedRestoreContext = null;
        CurrentState before = await CurrentAsync(context, timeout, token); CheckRestoreState(context, before);
        var saved = await SavedAsync(context, RestoreScope.Plan.Slot, timeout, token);
        if (!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(saved.Sysex), savedBaseHash))
            throw new InvalidDataException("Fresh saved base does not match the authorized current capture.");
        var live = await EditBufferAsync(context, timeout, token);
        if (!live.Image.RawImage.SequenceEqual(saved.Image.RawImage) || !live.Image.Body.SequenceEqual(saved.Image.Body))
            throw new InvalidDataException("Current buffer differs from saved base; preserve unsaved work before restoration.");
        CurrentState after = await CurrentAsync(context, timeout, token); CheckRestoreState(context, after);
        if (before != after) throw new InvalidDataException("Selection changed during restore preflight.");
        preparedRestoreContext = context;
        preparedRestoreBaseHash = savedBaseHash;
        return saved;
    }
    /// <summary>One paced temporary-buffer upload. Delivery is unverified until complete edit-buffer readback.</summary>
    public async Task UploadPresetAsync(TimeSpan timeout, TimeSpan frameSpacing, CancellationToken token)
    {
        var scope = RestoreScope;
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30) || frameSpacing < TimeSpan.Zero || frameSpacing > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var context = preparedRestoreContext ?? throw new InvalidOperationException("Fresh saved-base and live-buffer preflight is required before upload.");
        // Preflight can precede an operator checkpoint. Recheck bytes immediately before the one upload.
        await PreparePresetRestoreAsync(context, preparedRestoreBaseHash!, timeout, token);
        CheckRestoreState(context, await CurrentAsync(context, timeout, token));
        await gate.WaitAsync(token);
        try
        {
            lock (receiveLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (fault != null) throw new IOException("Session is unusable; reconcile after reconnecting.", fault);
                if (uploadAttempted) throw new InvalidOperationException("Upload already attempted; no replay.");
                uploadAttempted = true;
                while (replies.Reader.TryRead(out _)) { }
                assembler.Reset(); active = true; uploadMonitoring = true;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
            try
            {
                for (int index = 0; index < scope.Plan.FrameCount; index++)
                {
                    lock (receiveLock) { if (fault != null) throw new IOException("Upload rejected or uncertain; no further frames.", fault); }
                    await ((IPresetRestoreTransport)transport).SendPresetFrameAsync(scope.Plan, index, deadline.Token);
                    if (index + 1 < scope.Plan.FrameCount) await Task.Delay(frameSpacing, deadline.Token);
                }
                await Task.Delay(200, deadline.Token); // bounded observation policy, not an acknowledgement
                lock (receiveLock)
                {
                    if (fault != null) throw new IOException("Upload rejected or uncertain; verify after reconnecting.", fault);
                    uploadComplete = true;
                }
            }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested)
            { Fail(error); throw new TimeoutException("Preset upload timed out; no automatic replay.", error); }
            catch (Exception error) { Fail(error); throw; }
            finally { lock (receiveLock) { active = false; uploadMonitoring = false; assembler.Reset(); } }
        }
        finally { gate.Release(); }
    }
    public async Task<EditBufferCapture> VerifyUploadedPresetAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = RestoreScope;
        if (!uploadComplete) throw new InvalidOperationException("A complete upload attempt is required before verification.");
        CurrentState before = await CurrentAsync(context, timeout, token);
        if (!restoreReloadAttempted && before.Slot == scope.Plan.Slot && before.Scene != scope.Plan.ExpectedScene &&
            scope.Plan.BeforeScene != scope.Plan.ExpectedScene)
        {
            var savedBeforeAlignment = await SavedAsync(context, scope.Plan.Slot, timeout, token);
            var loadedBeforeAlignment = await EditBufferAsync(context, timeout, token);
            if (before != await CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection changed before upload scene alignment.");
            scope.PrepareUploadSceneAlignment(before, savedBeforeAlignment, loadedBeforeAlignment, preparedRestoreBaseHash!);
            await SendEditorAsync(EditorCommand.SelectScene(scope.Device, scope.Plan.Slot, scope.Plan.ExpectedScene), timeout, token);
            before = await CurrentAsync(context, timeout, token);
        }
        if (restoreReloadAttempted && before.Slot == scope.Plan.Slot && before.Scene != scope.Plan.ExpectedScene)
        {
            var savedAfterReload = await SavedAsync(context, scope.Plan.Slot, timeout, token);
            var liveAfterReload = await EditBufferAsync(context, timeout, token);
            if (before != await CurrentAsync(context, timeout, token))
                throw new InvalidDataException("Selection changed during reload scene alignment preflight.");
            scope.PrepareSceneAlignment(before, savedAfterReload, liveAfterReload);
            await SendEditorAsync(EditorCommand.SelectScene(scope.Device, scope.Plan.Slot, scope.Plan.ExpectedScene), timeout, token);
            before = await CurrentAsync(context, timeout, token);
        }
        CheckRestoreState(context, before);
        var capture = await EditBufferAsync(context, timeout, token);
        CurrentState after = await CurrentAsync(context, timeout, token);
        CheckRestoreState(context, after);
        if (before != after) throw new InvalidDataException("Selection changed during uploaded-buffer verification.");
        scope.VerifyBuffer(capture.Image);
        return capture;
    }
    public async Task<SavedCapture> VerifyRestoredSavedAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = RestoreScope;
        if (!saveAttempted) throw new InvalidOperationException("Save must be attempted before persistence verification.");
        CurrentState before = await CurrentAsync(context, timeout, token); CheckRestoreState(context, before);
        var capture = await SavedAsync(context, scope.Plan.Slot, timeout, token);
        CurrentState after = await CurrentAsync(context, timeout, token); CheckRestoreState(context, after);
        if (before != after) throw new InvalidDataException("Selection changed during saved-preset verification.");
        scope.VerifySaved(capture);
        scope.Completed=restoreReloadAttempted&&scope.BufferVerified;
        return capture;
    }

    private void AuthorizeEditor(EditorScope scope)
    {
        lock (receiveLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active || pilotScope != null || fault != null) throw new InvalidOperationException("Cannot change session authorization.");
            if (transport is not IEditorTransport) throw new InvalidOperationException("Transport cannot execute typed editor commands.");
            if (scope is PresetRestoreScope restore) restore.ClaimSession();
            if (scope is BlockInsertionScope insertion) insertion.ClaimSession();
            if (scope is BlockRemovalScope removal) removal.ClaimSession();
            if (scope is SceneMidiEditScope midiEdit) midiEdit.ClaimSession();
            if (scope is SceneMidiReconcileScope reconcile) reconcile.ClaimSession();
            if (scope is PresetLoadScope load) load.ClaimSession();
            if (scope is LiveControlScope control) control.Claim();
            if (scope is PerformanceControlScope performance) performance.Claim();
            pilotScope = scope;
        }
    }
    /// <summary>Send once and observe a bounded rejection window. Return means sent-unverified: callers must read back.</summary>
    public async Task SendEditorAsync(EditorCommand command, TimeSpan timeout, CancellationToken token)
    {
        var scope = pilotScope ?? throw new InvalidOperationException("Session is read-only; no editor operation was authorized.");
        scope.Validate(command);
        if (command.Operation == EditorOperation.QueryTempo) throw new InvalidOperationException("Use the scoped tempo exchange for query correlation.");
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (scope is PresetRestoreScope)
        {
            if (command.Operation == EditorOperation.SavePreset && saveAttempted)
                throw new InvalidOperationException("Save was already attempted; reconcile by readback, never replay.");
            if (!uploadComplete || preparedRestoreContext == null)
                throw new InvalidOperationException("This session has no completed restore upload.");
            if (command.Operation == EditorOperation.SelectScene)
            {
                if (!restoreReloadAttempted && !RestoreScope.UploadSceneAlignment) throw new InvalidOperationException("Scene alignment requires a reviewed upload or completed Reload.");
                var alignmentState = await CurrentAsync(preparedRestoreContext, timeout, token);
                var alignmentSaved = await SavedAsync(preparedRestoreContext, RestoreScope.Plan.Slot, timeout, token);
                var alignmentLive = await EditBufferAsync(preparedRestoreContext, timeout, token);
                RestoreScope.VerifySceneAlignmentBefore(alignmentState, alignmentSaved, alignmentLive);
                if (alignmentState != await CurrentAsync(preparedRestoreContext, timeout, token))
                    throw new InvalidDataException("Selection changed before reloaded scene alignment.");
            }
            else CheckRestoreState(preparedRestoreContext, await CurrentAsync(preparedRestoreContext, timeout, token));
            if (command.Operation == EditorOperation.SavePreset)
            {
                await VerifyUploadedPresetAsync(preparedRestoreContext, timeout, token);
                var savedBase = await SavedAsync(preparedRestoreContext, RestoreScope.Plan.Slot, timeout, token);
                if (PresetBody.Sha256(savedBase.Sysex) != preparedRestoreBaseHash)
                    throw new InvalidDataException("Saved base changed before Save; no overwrite permitted.");
                CheckRestoreState(preparedRestoreContext, await CurrentAsync(preparedRestoreContext, timeout, token));
            }
            if(sceneMidiUndoReceipt!=null&&command.Operation==EditorOperation.SavePreset)
                await CheckSceneMidiUndoSaveAsync(preparedRestoreContext,timeout,token);
        }
        if (scope is BlockInsertionScope insertionScope)
            await CheckInsertionCommandAsync(insertionScope, command, timeout, token);
        if (scope is BlockRemovalScope removalScope) await CheckRemovalCommandAsync(removalScope, command, timeout, token);
        if (scope is SceneMidiEditScope) await CheckMidiCommandAsync(command,timeout,token);
        if (scope is SceneMidiReconcileScope) await CheckReconcileCommandAsync(timeout,token);
        if (scope is PresetLoadScope plannedLoad)
        {
            plannedLoad.Expect(command);
            await CheckPresetLoadAsync(presetLoadContext??throw new InvalidOperationException("Prepare explicit load first."),timeout,token);
            if (plannedLoad.NextIsSceneAlignment && plannedLoad.AlignedAfterRecall)
            {
                lock(receiveLock)
                {
                    ObjectDisposedException.ThrowIf(disposed,this);
                    if(fault!=null)throw new IOException("Unusable load session; reconcile by readback.",fault);
                    plannedLoad.Consume(command); plannedLoad.Ready=false;
                }
                return;
            }
        }
        if (scope is LiveControlScope controlScope) await controlScope.CheckBefore(this, new(controlScope.Device, controlScope.Firmware, "scope"), timeout, token);
        if (scope is PerformanceControlScope performanceScope) await performanceScope.CheckBefore(this, new(performanceScope.Device, performanceScope.Firmware, "scope"), timeout, token);
        await gate.WaitAsync(token);
        try
        {
            lock (receiveLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (fault != null) throw new IOException("Session is unusable; reconcile by readback after reconnecting.", fault);
                if (scope is PresetSaveScope || scope is PresetRestoreScope or BlockInsertionScope or BlockRemovalScope or SceneMidiEditScope && command.Operation == EditorOperation.SavePreset)
                {
                    if (saveAttempted) throw new InvalidOperationException("Save was already attempted; reconcile by readback, never replay.");
                    saveAttempted = true;
                }
                if (scope is PresetRestoreScope && command.Operation == EditorOperation.ReloadPreset)
                {
                    if (restoreReloadAttempted) throw new InvalidOperationException("Reload already attempted; no replay.");
                    restoreReloadAttempted = true;
                    ((PresetRestoreScope)scope).Reloading();
                }
                if (scope is PresetRestoreScope aligned && command.Operation == EditorOperation.SelectScene)
                    aligned.ConsumeSceneAlignment();
                if (scope is BlockInsertionScope) ConsumeInsertionAttempt(command);
                if (scope is BlockRemovalScope) ConsumeRemovalAttempt(command);
                if (scope is SceneMidiEditScope) ConsumeMidiAttempt(command);
                if (scope is SceneMidiReconcileScope) reconcileLoads++;
                if (scope is PresetLoadScope loadScope)
                { loadScope.Consume(command); }
                if (scope is LiveControlScope liveScope)
                { if (liveScope.Attempted) throw new InvalidOperationException("Live control already attempted; no replay."); liveScope.Attempted = true; }
                if (scope is PerformanceControlScope performance)
                { if (performance.Attempted) throw new InvalidOperationException("Performance command already attempted; no replay."); performance.Attempted = true; }
                while (replies.Reader.TryRead(out _)) { }
                assembler.Reset(); active = true;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
            try
            {
                await ((IEditorTransport)transport).SendEditorAsync(command, deadline.Token);
                await Task.Delay(100, deadline.Token); // observation policy, not a device acknowledgement or manufacturer requirement
                lock (receiveLock) { if (fault != null) throw new IOException("Editor result is uncertain; reconcile by readback.", fault); }
                if (scope is BlockInsertionScope selected && command.Operation == EditorOperation.SelectCell) selected.CellSelected = true;
                if (scope is BlockRemovalScope removed && command.Operation == EditorOperation.SelectCell) removed.CellSelected = true;
            }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested)
            { Fail(error); throw new TimeoutException("Editor send timed out; no automatic replay was sent.", error); }
            catch (Exception error) { Fail(error); throw; }
            finally
            {
                lock (receiveLock)
                {
                    active = false;
                    if(scope is PresetLoadScope load && load.Attempted)load.Ready=false;
                    if(scope is PresetRestoreScope aligned && command.Operation == EditorOperation.SelectScene)
                        aligned.FinishSceneAlignmentSend();
                }
            }
        }
        finally { gate.Release(); }
    }
    public async Task<ReadResult> SnapshotAsync(DeviceContext context, int[] slots, TimeSpan timeout, CancellationToken token)
    {
        if (slots.Length == 0 || slots.Distinct().Count() != slots.Length) throw new ArgumentException("Select distinct preset slots.");
        foreach (int slot in slots) context.Definition.ValidateSlot(slot);
        CurrentState before = await CurrentAsync(context, timeout, token);
        List<SavedCapture> captures = [];
        foreach (int slot in slots.Order()) captures.Add(await SavedAsync(context, slot, timeout, token));
        CurrentState after = await CurrentAsync(context, timeout, token);
        return new(context, before, after, captures.ToArray());
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { lock (receiveLock) { disposed = true; active = false; } transport.Received -= Receive; transport.Faulted -= Fail; await transport.DisposeAsync(); }
        finally { gate.Release(); }
    }
}
