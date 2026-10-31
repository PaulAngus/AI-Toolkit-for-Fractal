namespace Fractal.Core;

public sealed partial class ReadSession
{
    private DeviceContext? insertionContext;
    private bool insertionLoadAttempted, insertionAttempted, insertionReloadAttempted;
    private BlockInsertionScope InsertionScope => pilotScope as BlockInsertionScope ?? throw new InvalidOperationException("No block insertion scope.");
    public void AuthorizeBlockInsertion(BlockInsertionScope scope) => AuthorizeEditor(scope);
    private static void CheckInsertionState(BlockInsertionScope scope, DeviceContext context, CurrentState state)
    {
        if (context.Definition != scope.Device || state.Slot != scope.Plan.Slot || state.Scene != scope.Plan.SavedScene)
            throw new InvalidDataException("Insertion requires its exact selected target and original saved scene.");
    }
    public async Task PrepareBlockInsertionAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = InsertionScope;
        if (insertionLoadAttempted || insertionAttempted) throw new InvalidOperationException("Insertion preparation already attempted; no replay.");
        if (scope.Device != context.Definition) throw new InvalidDataException("Wrong insertion model.");
        insertionContext = context;
        // Navigation must never discard unknown unsaved work, even outside the destination range.
        var state = await CurrentAsync(context, timeout, token);
        var currentSaved = await SavedAsync(context, state.Slot, timeout, token);
        var currentLive = await EditBufferAsync(context, timeout, token);
        if (!currentSaved.Image.RawImage.SequenceEqual(currentLive.Image.RawImage) || !currentSaved.Image.Body.SequenceEqual(currentLive.Image.Body) ||
            state != await CurrentAsync(context, timeout, token))
            throw new InvalidDataException("Preserve unsaved current work before loading the insertion target.");
        scope.Plan.VerifyBase(await SavedAsync(context, scope.Plan.Slot, timeout, token));
        scope.CanLoad = true;
        await SendEditorAsync(EditorCommand.ReloadPreset(scope.Device, scope.Plan.Slot), timeout, token);
        scope.CanLoad = false;
        var loadedState = await CurrentAsync(context, timeout, token); CheckInsertionState(scope, context, loadedState);
        scope.Plan.VerifyBase((await EditBufferAsync(context, timeout, token)).Image);
        scope.Plan.VerifyBase(await SavedAsync(context, scope.Plan.Slot, timeout, token));
        if (loadedState != await CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection changed during insertion preflight.");
        scope.Prepared = true;
    }
    private async Task CheckInsertionCommandAsync(BlockInsertionScope scope, EditorCommand command, TimeSpan timeout, CancellationToken token)
    {
        if (command.Operation == EditorOperation.SavePreset && saveAttempted || command.Operation == EditorOperation.InsertBlock && insertionAttempted ||
            command.Operation == EditorOperation.ReloadPreset && (scope.CanLoad ? insertionLoadAttempted : insertionReloadAttempted))
            throw new InvalidOperationException("Insertion/load/save/reload already attempted; no replay.");
        var context = insertionContext ?? throw new InvalidOperationException("Insertion needs complete saved/live preflight.");
        if (command.Operation == EditorOperation.ReloadPreset && scope.CanLoad) return;
        CheckInsertionState(scope, context, await CurrentAsync(context, timeout, token));
        if (command.Operation is EditorOperation.SelectCell or EditorOperation.InsertBlock)
            scope.Plan.VerifyBase((await EditBufferAsync(context, timeout, token)).Image);
        if (command.Operation == EditorOperation.SavePreset)
        {
            // Recheck complete preservation immediately before Save, not just an earlier partial status query.
            scope.Plan.VerifyChange((await EditBufferAsync(context, timeout, token)).Image);
            scope.Plan.VerifyBase(await SavedAsync(context, scope.Plan.Slot, timeout, token));
            CheckInsertionState(scope, context, await CurrentAsync(context, timeout, token));
        }
    }
    private void ConsumeInsertionAttempt(EditorCommand command)
    {
        if (command.Operation == EditorOperation.InsertBlock)
        {
            if (insertionAttempted) throw new InvalidOperationException("Insertion already attempted; no replay.");
            insertionAttempted = true;
        }
        if (command.Operation == EditorOperation.ReloadPreset)
        {
            if (InsertionScope.CanLoad)
            { if (insertionLoadAttempted) throw new InvalidOperationException("Target load already attempted; no replay."); insertionLoadAttempted = true; }
            else
            { if (insertionReloadAttempted) throw new InvalidOperationException("Verification reload already attempted; no replay."); insertionReloadAttempted = true; }
        }
    }
    public async Task<EditBufferCapture> VerifyInsertedBufferAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = InsertionScope; scope.BufferVerified = false;
        if (!insertionAttempted) throw new InvalidOperationException("No insertion attempted.");
        var before = await CurrentAsync(context, timeout, token); CheckInsertionState(scope, context, before);
        var live = await EditBufferAsync(context, timeout, token); scope.Plan.VerifyChange(live.Image);
        var after = await CurrentAsync(context, timeout, token); CheckInsertionState(scope, context, after);
        if (before != after) throw new InvalidDataException("Selection changed during inserted-buffer verification.");
        scope.BufferVerified = true;
        return live;
    }
    public async Task<SavedCapture> VerifyInsertedSavedAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = InsertionScope; scope.SavedVerified = false;
        if (!saveAttempted) throw new InvalidOperationException("No insertion save attempted.");
        var before = await CurrentAsync(context, timeout, token); CheckInsertionState(scope, context, before);
        var saved = await SavedAsync(context, scope.Plan.Slot, timeout, token); scope.Plan.VerifyChange(saved.Image);
        var live = await EditBufferAsync(context, timeout, token);
        if (!saved.Image.RawImage.SequenceEqual(live.Image.RawImage) || !saved.Image.Body.SequenceEqual(live.Image.Body))
            throw new InvalidDataException("Saved and loaded insertion images differ.");
        var after = await CurrentAsync(context, timeout, token); CheckInsertionState(scope, context, after);
        if (before != after) throw new InvalidDataException("Selection changed during insertion persistence verification.");
        scope.SavedVerified = true;
        scope.Completed = insertionReloadAttempted && scope.BufferVerified;
        return saved;
    }
}
