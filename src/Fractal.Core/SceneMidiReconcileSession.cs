namespace Fractal.Core;

public sealed partial class ReadSession
{
    private DeviceContext? reconcileContext;
    private int reconcileLoads;
    private SceneMidiReconcileScope ReconcileScope=>pilotScope as SceneMidiReconcileScope??throw new InvalidOperationException("No reconciliation scope.");
    public void AuthorizeSceneMidiReconcile(SceneMidiReconcileScope scope)=>AuthorizeEditor(scope);
    private async Task CheckReconcileCommandAsync(TimeSpan timeout,CancellationToken token)
    {
        var scope=ReconcileScope;var context=reconcileContext??throw new InvalidOperationException("Reconciliation has not started.");
        if(reconcileLoads>=2)throw new InvalidOperationException("Reconciliation loads cannot be replayed.");
        var before=await CurrentAsync(context,timeout,token);
        var saved=await SavedAsync(context,before.Slot,timeout,token);var live=await EditBufferAsync(context,timeout,token);
        if(!saved.Image.RawImage.SequenceEqual(live.Image.RawImage)||!saved.Image.Body.SequenceEqual(live.Image.Body)||before!=await CurrentAsync(context,timeout,token))
            throw new InvalidDataException("Unsaved work prevents recovery navigation; no partial write is replayed.");
        VerifyReconciledSaved(await SavedAsync(context,scope.Plan.Slot,timeout,token));
        if(before!=await CurrentAsync(context,timeout,token))throw new InvalidDataException("Selection changed during recovery preflight.");
    }
    private void VerifyReconciledSaved(SavedCapture saved)
    {
        var s=ReconcileScope;
        if(PresetBody.Sha256(saved.Sysex)!=s.ExpectedHash)throw new InvalidDataException("Recovery saved hash changed.");
        s.Plan.VerifyProgress(saved.Image,s.Original?0:s.Plan.CommandCount);
    }
    /// <summary>No parameter write, Save or upload. Fresh saved/live checks precede each load.</summary>
    public async Task<(SavedCapture Saved,SavedCapture Reloaded)> VerifyRecoveredSceneMidiAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var scope=ReconcileScope;var p=scope.Plan;
        if(reconcileContext!=null||context.Definition!=p.Device||context.Firmware!=p.Firmware)
            throw new InvalidOperationException("Fresh reconciliation scope with exact model/firmware required.");
        reconcileContext=context;SavedCapture? first=null;int? extra=null;
        for(int n=0;n<2;n++)
        {
            scope.CanLoad=true;
            try{await SendEditorAsync(EditorCommand.ReloadPreset(p.Device,p.Slot),timeout,token);}finally{scope.CanLoad=false;}
            var before=await CurrentAsync(context,timeout,token);
            if(before.Slot!=p.Slot||before.Scene!=p.SavedScene)throw new InvalidDataException("Recovery target/scene mismatch.");
            var live=await EditBufferAsync(context,timeout,token);p.VerifyProgress(live.Image,scope.Original?0:p.CommandCount);
            var table=await SceneMidiAsync(context,timeout,token);
            if(!table.Fields.SequenceEqual(p.ExpectedFields(scope.Original?0:p.CommandCount))||extra!=null&&extra!=table.ExtraWord)
                throw new InvalidDataException("Independent recovered table or opaque word differs.");
            extra=table.ExtraWord;
            var saved=await SavedAsync(context,p.Slot,timeout,token);VerifyReconciledSaved(saved);
            var repeat=await SavedAsync(context,p.Slot,timeout,token);VerifyReconciledSaved(repeat);
            if(!saved.Sysex.SequenceEqual(repeat.Sysex)||!live.Image.RawImage.SequenceEqual(saved.Image.RawImage)||
                !live.Image.Body.SequenceEqual(saved.Image.Body)||before!=await CurrentAsync(context,timeout,token))
                throw new InvalidDataException("Recovered saved/live, repeated reads or selection disagree.");
            if(n==0)first=saved;
            else
            {
                if(!first!.Sysex.SequenceEqual(saved.Sysex))throw new InvalidDataException("Recovery Reload changed saved contents.");
                lock(receiveLock){if(disposed||fault!=null)throw new IOException("Unusable recovery session cannot complete.");}
                scope.Completed=true;return(first,saved);
            }
        }
        throw new InvalidOperationException("Reconciliation did not complete.");
    }
}
