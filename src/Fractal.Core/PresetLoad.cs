using System.Buffers.Binary;
namespace Fractal.Core;

/// <summary>Explicit caller-requested navigation, bound to retained current saved/loaded and target captures.</summary>
public sealed class PresetLoadScope : EditorScope
{
    public override DeviceDefinition Device { get; }
    public string Firmware { get; }
    public CurrentState Before { get; }
    public int TargetSlot=>target.Image.Slot;
    public int TargetScene=>BinaryPrimitives.ReadUInt16LittleEndian(target.Image.Body)+1;
    public bool DiscardChanges { get; }
    public MidiPresetMapping? MidiMapping { get; }
    private readonly EditorCommand[] commands;
    private int consumed;
    internal bool AlignedAfterRecall { get; private set; }
    public EditorCommand[] Commands => commands.ToArray();
    private readonly SavedCapture current,target;
    private readonly PresetFileInspection loaded;
    internal bool Ready,Attempted,Completed;
    private int claimed;
    public PresetLoadScope(DeviceDefinition device,string firmware,CurrentState before,byte[] currentSaved,byte[] currentLoaded,byte[] targetSaved,bool discardChanges, MidiPresetMapping? midiMapping = null)
    {
        if(device!=DeviceDefinition.Create(device.Model,device.PresetCapacity)||string.IsNullOrWhiteSpace(firmware))throw new ArgumentException("Explicit model/firmware required.");
        Device=device;Firmware=firmware;Before=before;DiscardChanges=discardChanges;
        current=SnapshotExport.DecodeFile(device,currentSaved.ToArray());target=SnapshotExport.DecodeFile(device,targetSaved.ToArray());
        loaded=PresetFile.Inspect(device,currentLoaded.ToArray());device.ValidateSlot(before.Slot);
        if(current.Image.Slot!=before.Slot||before.Scene is <1 or >8||TargetScene is <1 or >8||(loaded.HeaderPayload[0]<<7|loaded.HeaderPayload[1])<device.PresetCapacity)
            throw new InvalidDataException("Retain exact current state, addressed saved captures and temporary loaded capture.");
        if(!discardChanges&&(!current.Image.RawImage.SequenceEqual(loaded.RawImage)||!current.Image.Body.SequenceEqual(loaded.Body)))
            throw new InvalidDataException("Explicit discard authorization is required for loaded/saved differences.");
        MidiMapping = midiMapping; midiMapping?.Validate(TargetSlot);
        var recall = midiMapping == null ? new[] { EditorCommand.ReloadPreset(device, TargetSlot) } :
            new[] { EditorCommand.SendCc(device, TargetSlot, new(midiMapping.Channel, midiMapping.BankController, midiMapping.Bank), true), EditorCommand.Program(device, TargetSlot, midiMapping) };
        // Review an optional absolute scene selection even when the current scene
        // matches: recall can select a different scene on the target device.
        commands = [.. recall, EditorCommand.SelectScene(device, TargetSlot, TargetScene)];
    }
    internal void Expect(EditorCommand command)
    { if (consumed >= commands.Length || !command.Frame.SequenceEqual(commands[consumed].Frame)) throw new InvalidOperationException("Preset recall messages must be sent once in reviewed order."); }
    internal void Consume(EditorCommand command) { Expect(command); consumed++; Attempted = consumed == commands.Length; }
    internal bool NextIsSceneAlignment => consumed == commands.Length - 1 && commands[^1].Operation == EditorOperation.SelectScene;
    internal void ClaimSession(){if(Interlocked.Exchange(ref claimed,1)!=0)throw new InvalidOperationException("Preset load scope cannot be reused.");}
    internal void VerifyBefore(DeviceContext context,CurrentState state,SavedCapture saved,PresetFileInspection live,SavedCapture destination)
    {
        if(context.Definition!=Device||context.Firmware!=Firmware||state!=Before||!current.Sysex.SequenceEqual(saved.Sysex)||
            !loaded.RawImage.SequenceEqual(live.RawImage)||!loaded.Body.SequenceEqual(live.Body)||!target.Sysex.SequenceEqual(destination.Sysex))
            throw new InvalidDataException("Current state or retained current/target content changed before explicit load.");
    }
    internal void VerifyAfter(DeviceContext context,CurrentState state,EditBufferCapture live,SavedCapture saved,SavedCapture prior)
    {
        if(context.Definition!=Device||context.Firmware!=Firmware||state.Slot!=TargetSlot||state.Scene!=TargetScene||
            !target.Sysex.SequenceEqual(saved.Sysex)||!current.Sysex.SequenceEqual(prior.Sysex)||
            !PresetUploadPlan.EquivalentCompression(target.Image.RawImage,target.Image.Body,live.Image.RawImage,live.Image.Body))
            throw new InvalidDataException("Explicit load did not preserve saved images or load the complete target/default scene.");
    }
    internal void VerifyBeforeSceneAlignment(DeviceContext context,CurrentState state,EditBufferCapture live,SavedCapture saved,SavedCapture prior)
    {
        if (!NextIsSceneAlignment || context.Definition != Device || context.Firmware != Firmware ||
            state.Slot != TargetSlot || state.Scene is < 1 or > 8 ||
            !target.Sysex.SequenceEqual(saved.Sysex) || !current.Sysex.SequenceEqual(prior.Sysex))
            throw new InvalidDataException("Preset recall changed the target, saved images or selection before scene alignment.");
        byte[] expectedBody = target.Image.Body.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(expectedBody, (ushort)(state.Scene - 1));
        if (!PresetUploadPlan.EquivalentCompression(target.Image.RawImage, expectedBody, live.Image.RawImage, live.Image.Body))
            throw new InvalidDataException("Preset recall changed loaded content beyond the selected scene; no scene alignment was sent.");
        AlignedAfterRecall = state.Scene == TargetScene;
    }
    public override void Validate(EditorCommand c)
    {if(!Ready||c.Device!=Device||c.Slot!=TargetSlot||!commands.Any(v => v.Operation == c.Operation && v.Frame.SequenceEqual(c.Frame)))throw new InvalidOperationException("Only reviewed target recall messages are authorized.");}
}

public sealed partial class ReadSession
{
    private DeviceContext? presetLoadContext;
    private PresetLoadScope LoadScope=>pilotScope as PresetLoadScope??throw new InvalidOperationException("No preset load scope.");
    public void AuthorizePresetLoad(PresetLoadScope scope)=>AuthorizeEditor(scope);
    private async Task CheckPresetLoadAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var scope=LoadScope;var before=await CurrentAsync(context,timeout,token);
        if(scope.Attempted)throw new InvalidOperationException("Preset load was already attempted; no replay.");
        if (scope.NextIsSceneAlignment)
        {
            var liveAfterRecall=await EditBufferAsync(context,timeout,token);
            var targetAfterRecall=await SavedAsync(context,scope.TargetSlot,timeout,token);
            var priorAfterRecall=await SavedAsync(context,scope.Before.Slot,timeout,token);
            scope.VerifyBeforeSceneAlignment(context,before,liveAfterRecall,targetAfterRecall,priorAfterRecall);
        }
        else
        {
            var saved=await SavedAsync(context,scope.Before.Slot,timeout,token);var live=await EditBufferAsync(context,timeout,token);
            var target=await SavedAsync(context,scope.TargetSlot,timeout,token);
            scope.VerifyBefore(context,before,saved,live.Image,target);
        }
        if(before!=await CurrentAsync(context,timeout,token))throw new InvalidDataException("Selection changed before explicit load.");
    }
    public async Task PreparePresetLoadAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        if(presetLoadContext!=null||LoadScope.Attempted)throw new InvalidOperationException("Use a fresh preset load scope.");
        await CheckPresetLoadAsync(context,timeout,token);presetLoadContext=context;LoadScope.Ready=true;
    }
    public async Task<EditBufferCapture> VerifyPresetLoadAsync(DeviceContext context,TimeSpan timeout,CancellationToken token)
    {
        var scope=LoadScope;if(!scope.Attempted)throw new InvalidOperationException("No load attempted.");
        var before=await CurrentAsync(context,timeout,token);var live=await EditBufferAsync(context,timeout,token);
        var saved=await SavedAsync(context,scope.TargetSlot,timeout,token);var prior=await SavedAsync(context,scope.Before.Slot,timeout,token);
        scope.VerifyAfter(context,before,live,saved,prior);
        if(before!=await CurrentAsync(context,timeout,token))throw new InvalidDataException("Selection changed after explicit load.");
        lock(receiveLock){if(disposed||fault!=null)throw new IOException("Unusable load session.");}scope.Completed=true;return live;
    }
}
