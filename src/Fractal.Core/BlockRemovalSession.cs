namespace Fractal.Core;

public sealed partial class ReadSession
{
    private DeviceContext? removalContext;
    private bool removalLoadAttempted, removalAttempted, removalReloadAttempted;
    private BlockRemovalScope RemovalScope => pilotScope as BlockRemovalScope ?? throw new InvalidOperationException("No block removal scope.");
    public void AuthorizeBlockRemoval(BlockRemovalScope scope) => AuthorizeEditor(scope);
    private static void CheckRemovalState(BlockRemovalScope scope, DeviceContext context, CurrentState state)
    {
        if (context.Definition != scope.Device || state.Slot != scope.Plan.Slot || state.Scene != scope.Plan.SavedScene)
            throw new InvalidDataException("Removal requires its exact selected target and original saved scene.");
    }
    public async Task PrepareBlockRemovalAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = RemovalScope;
        if (removalLoadAttempted || removalAttempted) throw new InvalidOperationException("Removal preparation already attempted; no replay.");
        if (scope.Device != context.Definition) throw new InvalidDataException("Wrong removal model.");
        removalContext = context;
        // Navigation must never discard unknown unsaved work, even outside the destination range.
        var state = await CurrentAsync(context, timeout, token);
        var currentSaved = await SavedAsync(context, state.Slot, timeout, token);
        var currentLive = await EditBufferAsync(context, timeout, token);
        if (!currentSaved.Image.RawImage.SequenceEqual(currentLive.Image.RawImage) || !currentSaved.Image.Body.SequenceEqual(currentLive.Image.Body) ||
            state != await CurrentAsync(context, timeout, token))
            throw new InvalidDataException("Preserve unsaved current work before loading the removal target.");
        scope.Plan.VerifyBase(await SavedAsync(context, scope.Plan.Slot, timeout, token));
        scope.CanLoad = true;
        await SendEditorAsync(EditorCommand.ReloadPreset(scope.Device, scope.Plan.Slot), timeout, token);
        scope.CanLoad = false;
        var loadedState = await CurrentAsync(context, timeout, token); CheckRemovalState(scope, context, loadedState);
        scope.Plan.VerifyBase((await EditBufferAsync(context, timeout, token)).Image);
        scope.Plan.VerifyBase(await SavedAsync(context, scope.Plan.Slot, timeout, token));
        if (loadedState != await CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection changed during removal preflight.");
        scope.Prepared = true;
    }
    private async Task CheckRemovalCommandAsync(BlockRemovalScope scope, EditorCommand command, TimeSpan timeout, CancellationToken token)
    {
        if (command.Operation == EditorOperation.SavePreset && saveAttempted || command.Operation == EditorOperation.ClearCell && removalAttempted ||
            command.Operation == EditorOperation.ReloadPreset && (scope.CanLoad ? removalLoadAttempted : removalReloadAttempted))
            throw new InvalidOperationException("Removal/load/save/reload already attempted; no replay.");
        var context = removalContext ?? throw new InvalidOperationException("Removal needs complete saved/live preflight.");
        if (command.Operation == EditorOperation.ReloadPreset && scope.CanLoad) return;
        CheckRemovalState(scope, context, await CurrentAsync(context, timeout, token));
        if (command.Operation is EditorOperation.SelectCell or EditorOperation.ClearCell)
        {
            scope.Plan.VerifyBase((await EditBufferAsync(context, timeout, token)).Image);
            scope.Plan.VerifyBase(await SavedAsync(context, scope.Plan.Slot, timeout, token));
            CheckRemovalState(scope, context, await CurrentAsync(context, timeout, token));
        }
        if (command.Operation == EditorOperation.SavePreset)
        {
            // Recheck complete preservation immediately before Save, not just an earlier partial status query.
            scope.Plan.VerifyChange((await EditBufferAsync(context, timeout, token)).Image);
            scope.Plan.VerifyBase(await SavedAsync(context, scope.Plan.Slot, timeout, token));
            CheckRemovalState(scope, context, await CurrentAsync(context, timeout, token));
        }
    }
    private void ConsumeRemovalAttempt(EditorCommand command)
    {
        if (command.Operation == EditorOperation.ClearCell)
        {
            if (removalAttempted) throw new InvalidOperationException("Removal already attempted; no replay.");
            removalAttempted = true;
        }
        if (command.Operation == EditorOperation.ReloadPreset)
        {
            if (RemovalScope.CanLoad)
            { if (removalLoadAttempted) throw new InvalidOperationException("Target load already attempted; no replay."); removalLoadAttempted = true; }
            else
            { if (removalReloadAttempted) throw new InvalidOperationException("Verification reload already attempted; no replay."); removalReloadAttempted = true; }
        }
    }
    public async Task<EditBufferCapture> VerifyRemovedBufferAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = RemovalScope; scope.BufferVerified = false;
        if (!removalAttempted) throw new InvalidOperationException("No removal attempted.");
        var before = await CurrentAsync(context, timeout, token); CheckRemovalState(scope, context, before);
        var live = await EditBufferAsync(context, timeout, token); scope.Plan.VerifyChange(live.Image);
        var after = await CurrentAsync(context, timeout, token); CheckRemovalState(scope, context, after);
        if (before != after) throw new InvalidDataException("Selection changed during removed-buffer verification.");
        scope.BufferVerified = true;
        return live;
    }
    public async Task<SavedCapture> VerifyRemovedSavedAsync(DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var scope = RemovalScope; scope.SavedVerified = false;
        if (!saveAttempted) throw new InvalidOperationException("No removal save attempted.");
        var before = await CurrentAsync(context, timeout, token); CheckRemovalState(scope, context, before);
        var saved = await SavedAsync(context, scope.Plan.Slot, timeout, token); scope.Plan.VerifyChange(saved.Image);
        var live = await EditBufferAsync(context, timeout, token);
        if (!saved.Image.RawImage.SequenceEqual(live.Image.RawImage) || !saved.Image.Body.SequenceEqual(live.Image.Body))
            throw new InvalidDataException("Saved and loaded removal images differ.");
        var after = await CurrentAsync(context, timeout, token); CheckRemovalState(scope, context, after);
        if (before != after) throw new InvalidDataException("Selection changed during removal persistence verification.");
        scope.SavedVerified = true;
        scope.Completed = removalReloadAttempted && scope.BufferVerified;
        return saved;
    }
}
