namespace Fractal.Core;

public sealed record SceneMidiRecoveryOutcome(int Applied,int VerifiedResults,int Undone,int AlreadyTerminal);

/// <summary>Explicit resume/undo, never automatic retry. The caller exclusively owns the transport.</summary>
public static class SceneMidiRecoveryRunner
{
    public static async Task<SceneMidiRecoveryOutcome> RunAsync(SceneMidiRecoveryStore store,IPresetRestoreTransport transport,
        SceneMidiRecoveryAuthority authority,bool undo,TimeSpan timeout,CancellationToken token,Action<SceneMidiRecoveryStore>? checkpoint=null)
    {
        if(authority.Device!=store.Device)throw new ArgumentException("Recovery authority model mismatch.");
        DeviceContext context;
        // Reconcile every target before the first write. This is a read snapshot, not a hardware lock.
        await using(var reads=new ReadSession(new Borrowed(transport)))
        {
            context=await reads.IdentifyAsync(store.Device.Model,store.Device.PresetCapacity,timeout,token);
            if(context.Firmware!=store.Firmware)throw new InvalidDataException("Exact recovery firmware mismatch.");
            await SafeSelection(reads,context,timeout,token);
            var before=await reads.CurrentAsync(context,timeout,token);
            foreach(var i in store.Items)
                if(store.Reconcile(i.Slot,await reads.SavedAsync(context,i.Slot,timeout,token),undo)=="conflict")
                    throw new InvalidDataException("A recovery target conflicts with its retained original/result or operation phase.");
            if(before!=await reads.CurrentAsync(context,timeout,token))throw new InvalidDataException("Selection changed during batch reconciliation.");
        }
        int applied=0,verified=0,undone=0,terminal=0;
        void Attempt(int slot,string operation){store.Attempt(slot,operation);checkpoint?.Invoke(store);}
        foreach(var item in store.Items)
        {
            token.ThrowIfCancellationRequested();var p=store.Plan(item.Slot);SavedCapture current;string action;
            await using(var reads=new ReadSession(new Borrowed(transport)))
            {
                current=await reads.SavedAsync(context,p.Slot,timeout,token);action=store.Reconcile(p.Slot,current,undo);
            }
            if(action=="conflict")throw new InvalidDataException("Recovery target changed since preflight.");
            if(action is "unchanged" or "undone"){terminal++;continue;}
            if(action is "verify-result" or "verify-original")
            {
                bool original=action=="verify-original";
                var scope=new SceneMidiReconcileScope(p,original,PresetBody.Sha256(current.Sysex));authority.Activate(scope);
                await using var session=new ReadSession(new Borrowed(transport));session.AuthorizeSceneMidiReconcile(scope);
                Attempt(p.Slot,action);
                var result=await session.VerifyRecoveredSceneMidiAsync(context,timeout,token);
                if(original){store.CompleteUndo(p.Slot,result.Saved.Sysex,result.Reloaded.Sysex);undone++;}
                else{store.CompleteEdit(p.Slot,result.Saved.Sysex,result.Reloaded.Sysex);verified++;}
                checkpoint?.Invoke(store);authority.Complete(scope);continue;
            }
            if(action=="apply")
            {
                var scope=new SceneMidiEditScope(p);authority.Activate(scope);
                await using var session=new ReadSession(new Borrowed(transport));session.AuthorizeSceneMidiEdit(scope);
                Attempt(p.Slot,"load");await session.PrepareSceneMidiEditAsync(context,timeout,token);
                for(int n=0;n<p.CommandCount;n++)
                {
                    Attempt(p.Slot,"field");await session.SendEditorAsync(p.Command(n),timeout,token);
                    await session.VerifySceneMidiFieldAsync(context,timeout,token);
                }
                await session.VerifySceneMidiBufferAsync(context,timeout,token);
                Attempt(p.Slot,"save");await session.SendEditorAsync(EditorCommand.SavePreset(p.Device,p.Slot),timeout,token);
                var saved=await session.VerifySceneMidiSavedAsync(context,timeout,token);await session.VerifySceneMidiSavedAsync(context,timeout,token);
                Attempt(p.Slot,"reload");await session.SendEditorAsync(EditorCommand.ReloadPreset(p.Device,p.Slot),timeout,token);
                await session.VerifySceneMidiBufferAsync(context,timeout,token);
                var reloaded=await session.VerifySceneMidiSavedAsync(context,timeout,token);_=session.SceneMidiReceipt();
                store.CompleteEdit(p.Slot,saved.Sysex,reloaded.Sysex);checkpoint?.Invoke(store);authority.Complete(scope);applied++;continue;
            }
            if(action!="undo")throw new InvalidOperationException("Unknown recovery operation.");
            var receipt=store.Receipt(p.Slot);
            // Restore targets must be safely loaded first; no upload can navigate away from unsaved work.
            var navigation=new SceneMidiReconcileScope(p,false,receipt.ResultSha256);authority.Activate(navigation);
            await using(var session=new ReadSession(new Borrowed(transport)))
            {
                session.AuthorizeSceneMidiReconcile(navigation);Attempt(p.Slot,"undo-load");
                await session.VerifyRecoveredSceneMidiAsync(context,timeout,token);
            }
            authority.Complete(navigation);
            var restore=new PresetRestoreScope(receipt.UndoUpload);authority.Activate(restore);
            await using(var session=new ReadSession(new Borrowed(transport)))
            {
                session.AuthorizePresetRestore(restore);await session.PrepareSceneMidiUndoAsync(receipt,context,timeout,token);
                Attempt(p.Slot,"undo-upload");await session.UploadPresetAsync(TimeSpan.FromSeconds(30),TimeSpan.FromMilliseconds(100),token);
                await session.VerifyUploadedPresetAsync(context,timeout,token);
                Attempt(p.Slot,"undo-save");await session.SendEditorAsync(EditorCommand.SavePreset(p.Device,p.Slot),timeout,token);
                var saved=await session.VerifyRestoredSavedAsync(context,timeout,token);await session.VerifyRestoredSavedAsync(context,timeout,token);
                Attempt(p.Slot,"undo-reload");await session.SendEditorAsync(EditorCommand.ReloadPreset(p.Device,p.Slot),timeout,token);
                await session.VerifyUploadedPresetAsync(context,timeout,token);var reloaded=await session.VerifyRestoredSavedAsync(context,timeout,token);
                store.CompleteUndo(p.Slot,saved.Sysex,reloaded.Sysex);checkpoint?.Invoke(store);
            }
            authority.Complete(restore);undone++;
        }
        return new(applied,verified,undone,terminal);
    }
    private static async Task SafeSelection(ReadSession reads,DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var before=await reads.CurrentAsync(context,timeout,token);
        var saved=await reads.SavedAsync(context,before.Slot,timeout,token);var live=await reads.EditBufferAsync(context,timeout,token);
        if(!saved.Image.RawImage.SequenceEqual(live.Image.RawImage)||!saved.Image.Body.SequenceEqual(live.Image.Body)||before!=await reads.CurrentAsync(context,timeout,token))
            throw new InvalidDataException("Unsaved work prevents batch recovery; resolve it explicitly before resuming.");
    }
    private sealed class Borrowed(IPresetRestoreTransport inner):IPresetRestoreTransport
    {
        public event Action<byte[]>? Received{add=>inner.Received+=value;remove=>inner.Received-=value;}
        public event Action<Exception>? Faulted{add=>inner.Faulted+=value;remove=>inner.Faulted-=value;}
        public Task SendReadAsync(byte[] query,CancellationToken token)=>inner.SendReadAsync(query,token);
        public Task SendEditorAsync(EditorCommand command,CancellationToken token)=>inner.SendEditorAsync(command,token);
        public Task SendPresetFrameAsync(PresetUploadPlan plan,int index,CancellationToken token)=>inner.SendPresetFrameAsync(plan,index,token);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
