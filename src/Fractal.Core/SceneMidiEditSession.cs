namespace Fractal.Core;

public sealed partial class ReadSession
{
    private DeviceContext? sceneMidiContext;
    private bool sceneMidiLoadAttempted,sceneMidiReloadAttempted;
    private readonly HashSet<int> sceneMidiAttempts=[];
    private int? sceneMidiExtraWord;
    private byte[]? sceneMidiSavedWire;
    private SceneMidiEditReceipt? sceneMidiUndoReceipt;
    private SceneMidiEditScope MidiEditScope=>pilotScope as SceneMidiEditScope??throw new InvalidOperationException("No Scene MIDI edit scope.");
    public void AuthorizeSceneMidiEdit(SceneMidiEditScope scope)=>AuthorizeEditor(scope);
    private void CheckMidiState(DeviceContext context,CurrentState state)
    {
        var p=MidiEditScope.Plan;
        if(context.Definition!=p.Device||context.Firmware!=p.Firmware||state.Slot!=p.Slot||state.Scene!=p.SavedScene)
            throw new InvalidDataException("Scene MIDI edit requires its exact model/firmware, selected target and saved scene.");
    }
    public async Task PrepareSceneMidiEditAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var scope=MidiEditScope;var p=scope.Plan;
        if(p.Unchanged)throw new InvalidOperationException("Already-correct plans need no navigation or writes.");
        if(sceneMidiLoadAttempted||sceneMidiAttempts.Count!=0)throw new InvalidOperationException("Preparation already attempted; no replay.");
        if(context.Definition!=p.Device||context.Firmware!=p.Firmware)throw new InvalidDataException("Scene MIDI model/firmware mismatch.");
        sceneMidiContext=context;
        var before=await CurrentAsync(context,timeout,token);
        var saved=await SavedAsync(context,before.Slot,timeout,token);var live=await EditBufferAsync(context,timeout,token);
        if(!saved.Image.RawImage.SequenceEqual(live.Image.RawImage)||!saved.Image.Body.SequenceEqual(live.Image.Body)||
            before!=await CurrentAsync(context,timeout,token))throw new InvalidDataException("Preserve unsaved work before loading a Scene MIDI target.");
        p.VerifyBase(await SavedAsync(context,p.Slot,timeout,token));
        scope.CanLoad=true;
        await SendEditorAsync(EditorCommand.ReloadPreset(p.Device,p.Slot),timeout,token);scope.CanLoad=false;
        var loaded=await CurrentAsync(context,timeout,token);CheckMidiState(context,loaded);
        p.VerifyBase((await EditBufferAsync(context,timeout,token)).Image);
        p.VerifyBase(await SavedAsync(context,p.Slot,timeout,token));
        var table=await SceneMidiAsync(context,timeout,token);
        if(!table.Fields.SequenceEqual(p.ExpectedFields(0)))throw new InvalidDataException("Live Scene MIDI table differs from saved base.");
        sceneMidiExtraWord=table.ExtraWord;
        if(loaded!=await CurrentAsync(context,timeout,token))throw new InvalidDataException("Selection changed during Scene MIDI preflight.");
        scope.Prepared=true;
    }
    private async Task CheckMidiCommandAsync(EditorCommand command,TimeSpan timeout,CancellationToken token)
    {
        var scope=MidiEditScope;var context=sceneMidiContext??throw new InvalidOperationException("Complete Scene MIDI preflight is required.");
        if(command.Operation==EditorOperation.SavePreset&&saveAttempted||command.Operation==EditorOperation.SetSceneMidiField&&sceneMidiAttempts.Contains(scope.NextCommand)||
            command.Operation==EditorOperation.ReloadPreset&&(scope.CanLoad?sceneMidiLoadAttempted:sceneMidiReloadAttempted))
            throw new InvalidOperationException("Scene MIDI command already attempted; no replay.");
        if(command.Operation==EditorOperation.ReloadPreset&&scope.CanLoad)return;
        CheckMidiState(context,await CurrentAsync(context,timeout,token));
        if(command.Operation is EditorOperation.SetSceneMidiField or EditorOperation.SavePreset)
        {
            await VerifyMidiProgressCoreAsync(context,scope.NextCommand,timeout,token);
            scope.Plan.VerifyBase(await SavedAsync(context,scope.Plan.Slot,timeout,token));
            CheckMidiState(context,await CurrentAsync(context,timeout,token));
        }
    }
    private void ConsumeMidiAttempt(EditorCommand command)
    {
        var scope=MidiEditScope;
        if(command.Operation==EditorOperation.SetSceneMidiField)
        {
            if(!sceneMidiAttempts.Add(scope.NextCommand))throw new InvalidOperationException("Field write already attempted; no replay.");
            scope.BufferVerified=false;
        }
        if(command.Operation==EditorOperation.ReloadPreset)
        {
            if(scope.CanLoad){if(sceneMidiLoadAttempted)throw new InvalidOperationException("Load already attempted.");sceneMidiLoadAttempted=true;}
            else{if(sceneMidiReloadAttempted)throw new InvalidOperationException("Reload already attempted.");sceneMidiReloadAttempted=true;}
        }
    }
    private async Task<EditBufferCapture> VerifyMidiProgressCoreAsync(DeviceContext context,int count,TimeSpan timeout,CancellationToken token)
    {
        var p=MidiEditScope.Plan;var before=await CurrentAsync(context,timeout,token);CheckMidiState(context,before);
        var live=await EditBufferAsync(context,timeout,token);p.VerifyProgress(live.Image,count);
        var table=await SceneMidiAsync(context,timeout,token);
        if(!table.Fields.SequenceEqual(p.ExpectedFields(count))||sceneMidiExtraWord!=table.ExtraWord)
            throw new InvalidDataException("Live MIDI fields or opaque extra word differ from intent.");
        var after=await CurrentAsync(context,timeout,token);CheckMidiState(context,after);
        if(before!=after)throw new InvalidDataException("Selection changed during Scene MIDI verification.");
        return live;
    }
    /// <summary>Verify one sent field before enabling the next. A field is never replayed to resolve uncertainty.</summary>
    public async Task VerifySceneMidiFieldAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var scope=MidiEditScope;
        int pending=scope.NextCommand;
        if(!sceneMidiAttempts.Contains(pending))throw new InvalidOperationException("No pending field write.");
        await VerifyMidiProgressCoreAsync(context,pending+1,timeout,token);
        lock(receiveLock)
        {
            if(scope.NextCommand!=pending)throw new InvalidOperationException("A field was already verified by another caller.");
            scope.NextCommand++;
        }
    }
    public async Task<EditBufferCapture> VerifySceneMidiBufferAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var scope=MidiEditScope;scope.BufferVerified=false;
        if(!scope.Prepared||scope.NextCommand!=scope.Plan.CommandCount)throw new InvalidOperationException("All planned fields must be sent and verified.");
        var live=await VerifyMidiProgressCoreAsync(context,scope.NextCommand,timeout,token);scope.BufferVerified=true;return live;
    }
    public async Task<SavedCapture> VerifySceneMidiSavedAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var scope=MidiEditScope;scope.SavedVerified=false;
        if(!saveAttempted)throw new InvalidOperationException("No Scene MIDI Save attempted.");
        var live=await VerifyMidiProgressCoreAsync(context,scope.Plan.CommandCount,timeout,token);
        var saved=await SavedAsync(context,scope.Plan.Slot,timeout,token);scope.Plan.VerifyProgress(saved.Image,scope.Plan.CommandCount);
        if(!saved.Image.RawImage.SequenceEqual(live.Image.RawImage)||!saved.Image.Body.SequenceEqual(live.Image.Body))
            throw new InvalidDataException("Complete saved and loaded MIDI preset differ.");
        CheckMidiState(context,await CurrentAsync(context,timeout,token));
        if(sceneMidiSavedWire!=null&&!saved.Sysex.SequenceEqual(sceneMidiSavedWire))throw new InvalidDataException("Repeated saved MIDI result differs.");
        sceneMidiSavedWire=saved.Sysex.ToArray();scope.SavedVerified=true;scope.Completed=sceneMidiReloadAttempted&&scope.BufferVerified;
        return saved;
    }
    public SceneMidiEditReceipt SceneMidiReceipt()
    {
        var scope=MidiEditScope;
        lock(receiveLock){if(disposed||fault!=null)throw new InvalidOperationException("An unusable session cannot issue an undo receipt.");}
        if(!scope.Completed||!scope.SavedVerified||!scope.BufferVerified||sceneMidiSavedWire==null)throw new InvalidOperationException("Undo receipt requires verified saved/reloaded completion.");
        return new(scope.Plan,SnapshotExport.DecodeFile(scope.Device,sceneMidiSavedWire));
    }
    public async Task PrepareSceneMidiUndoAsync(SceneMidiEditReceipt receipt,DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        sceneMidiUndoReceipt=null;
        if(!ReferenceEquals(RestoreScope.Plan,receipt.UndoUpload)||context.Firmware!=receipt.Plan.Firmware)
            throw new InvalidOperationException("Undo requires its exact receipt upload and firmware.");
        receipt.VerifyUndoBase(await SavedAsync(context,receipt.Plan.Slot,timeout,token));
        await PreparePresetRestoreAsync(context,receipt.ResultSha256,timeout,token);
        sceneMidiUndoReceipt=receipt;
    }
    private async Task CheckSceneMidiUndoSaveAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var r=sceneMidiUndoReceipt!;
        r.VerifyUndoBase(await SavedAsync(context,r.Plan.Slot,timeout,token));
        r.UndoUpload.Verify((await EditBufferAsync(context,timeout,token)).Image);
        CheckRestoreState(context,await CurrentAsync(context,timeout,token));
    }
}
