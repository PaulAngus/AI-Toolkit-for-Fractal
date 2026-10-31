using System.Buffers.Binary;
using System.Text.Json;
namespace Fractal.Core;

public sealed record SceneMidiMessageChange(int Scene, int MessageSlot, string Type, int Channel, int Value, int? Controller = null);

/// <summary>Existing-block edits only: fill Off rows, reuse exact messages, never replace an occupied row.</summary>
public sealed class SceneMidiEditPlan
{
    public DeviceDefinition Device { get; }
    public string Firmware { get; }
    public int Slot => baseline.Image.Slot;
    public int SavedScene { get; }
    public string BaseSha256 { get; }
    public int CommandCount => commands.Length;
    public bool Unchanged => CommandCount == 0;
    private readonly SavedCapture baseline;
    private readonly int tableOffset;
    private readonly int[] originalFields;
    private readonly (int Parameter, int Value)[] fields;
    private readonly EditorCommand[] commands;
    private readonly SceneMidiMessageChange[] changes;
    public SceneMidiMessageChange[] Changes => changes.ToArray();
    private SceneMidiEditPlan(DeviceDefinition d, string firmware, SavedCapture saved, int offset, int[] original,
        (int Parameter, int Value)[] writes, SceneMidiMessageChange[] requested)
    {
        Device=d; Firmware=firmware; baseline=saved; tableOffset=offset; originalFields=original;
        BaseSha256=PresetBody.Sha256(saved.Sysex);
        SavedScene=BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body)+1;
        fields=writes; commands=writes.Select(w=>EditorCommand.SetSceneMidiField(d,Slot,w.Parameter,w.Value)).ToArray();
        changes=requested.ToArray();
    }
    public static SceneMidiEditPlan Create(DeviceDefinition d, string firmware, byte[] savedWire, string wireHash,
        IEnumerable<SceneMidiMessageChange> changes)
    {
        if(d!=DeviceDefinition.Create(d.Model,d.PresetCapacity)||string.IsNullOrWhiteSpace(firmware))
            throw new ArgumentException("A contained model definition and explicit firmware are required.");
        byte[] copy=savedWire.ToArray();
        if(!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(copy),wireHash))throw new InvalidDataException("Scene MIDI baseline hash mismatch.");
        var saved=SnapshotExport.DecodeFile(d,copy); var snapshot=PresetBody.Decode(d,saved.Image);
        if(!snapshot.SceneMidiPresent||snapshot.SceneMidiDecodeStatus!="decoded"||snapshot.Limitations.Length!=0)
            throw new InvalidDataException("Scene MIDI execution requires one existing, fully decoded block.");
        var l=d.SceneMidi!; var record=snapshot.BlockRecords.Single(r=>r.Columns==l.Columns&&r.Rows==l.Rows);
        if(record.Offset<12||BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body.AsSpan(record.Offset-12,2))!=190)
            throw new InvalidDataException("Scene MIDI instance prefix is not established.");
        int scene=BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body)+1;
        if(scene is <1 or >8)throw new InvalidDataException("Saved scene field is outside 1–8.");
        _=PresetUploadPlan.Create(d,saved.Image.Slot,scene,copy,wireHash); // establish backup upload compatibility before editing
        var requested=changes.ToArray();
        if(requested.Length==0||requested.Length>64||requested.Select(c=>(c.Scene,c.MessageSlot)).Distinct().Count()!=requested.Length)
            throw new ArgumentException("Choose distinct explicit scene/message rows.");
        var original=SceneMidiTable.SavedFields(d,snapshot.BlockRecords);var decoded=SceneMidiTable.Decode(d,original);
        var writes=new List<(int Parameter,int Value)>();
        foreach(var c in requested.OrderBy(c=>c.Scene).ThenBy(c=>c.MessageSlot))
        {
            if(c.Scene is <1 or >8||c.MessageSlot is <1 or >8||c.Channel is <1 or >16||c.Value is <0 or >127||
                c.Type is not ("PC" or "CC")||c.Type=="CC"&&(c.Controller is null or <0 or >127)||c.Type=="PC"&&c.Controller!=null)
                throw new ArgumentException("Invalid Scene MIDI message.");
            var before=decoded[c.Scene-1][c.MessageSlot-1];
            if(before.Type==c.Type&&before.Channel==c.Channel&&before.Value==c.Value&&before.Controller==c.Controller)continue;
            if(before.Type!="Off")throw new InvalidDataException("Occupied Scene MIDI row conflicts with the request.");
            int i=(c.Scene-1)*8+c.MessageSlot-1;
            void Add(int parameter,int value){if(original[parameter]!=value)writes.Add((parameter,value));}
            // Prepare an inactive row before activating its type. PC's unused controller stays exact.
            Add(l.ChannelBank+i,c.Channel-1);
            if(c.Type=="CC")Add(l.ControllerBank+i,c.Controller!.Value);
            Add(l.ValueBank+i,c.Value); Add(l.TypeBank+i,c.Type=="PC"?2:1);
        }
        return new(d,firmware,saved,record.Offset+l.FirstWord*2,original,writes.ToArray(),requested);
    }
    /// <summary>Consume an existing-block target from the version-3 snapshot/schema-2 Python planner contract.</summary>
    public static SceneMidiEditPlan FromPlanner(DeviceDefinition d, string firmware, byte[] savedWire, string wireHash,
        JsonElement envelope, int slot)
    {
        if(envelope.GetProperty("schema_version").GetInt32()!=2||envelope.GetProperty("source_schema_version").GetInt32()!=3||
            envelope.GetProperty("mode").GetString()!="offline-plan-only")throw new InvalidDataException("Execution requires a schema-2 plan from complete schema-3 data.");
        var dev=envelope.GetProperty("device");
        if(dev.GetProperty("model").GetString()!=d.Model||dev.GetProperty("firmware").GetString()!=firmware||
            dev.GetProperty("preset_capacity").GetInt32()!=d.PresetCapacity)throw new InvalidDataException("Planner model/firmware/capacity mismatch.");
        var target=envelope.GetProperty("presets").EnumerateArray().Single(p=>p.GetProperty("slot").GetInt32()==slot);
        string? status=target.GetProperty("status").GetString();
        if(status is not ("planned" or "unchanged")||target.GetProperty("conflicts").GetArrayLength()!=0)
            throw new InvalidDataException("Planner conflicts/skipped targets are not executable.");
        var saved=SnapshotExport.DecodeFile(d,savedWire);
        if(saved.Image.Slot!=slot||target.GetProperty("base_content_sha256").GetString()!=PresetBody.Sha256(saved.Image.RawImage))
            throw new InvalidDataException("Planner saved base mismatch.");
        var m=envelope.GetProperty("message");string type=m.GetProperty("type").GetString()!;
        int channel=m.GetProperty("channel").GetInt32(),value=m.GetProperty("value").GetInt32();
        int? controller=m.TryGetProperty("controller",out var cc)?cc.GetInt32():null;
        int messageSlot=envelope.GetProperty("message_slot").GetInt32();
        var scenes=envelope.GetProperty("scenes").EnumerateArray().Select(s=>s.GetInt32()).ToArray();
        var changes=scenes.Select(s=>new SceneMidiMessageChange(s,messageSlot,type,channel,value,controller)).ToArray();
        var plan=Create(d,firmware,savedWire,wireHash,changes);
        var snapshot=PresetBody.Decode(d,saved.Image);var expected=changes.Where(c=>snapshot.Scenes[c.Scene-1].Messages[c.MessageSlot-1].Type=="Off").ToArray();
        var actions=target.GetProperty("actions").EnumerateArray().ToArray();
        if(actions.Length!=expected.Length||(plan.Unchanged!=(status=="unchanged")))throw new InvalidDataException("Planner action/status mismatch.");
        var seen=new HashSet<(int,int)>();
        foreach(var a in actions)
        {
            int s=a.GetProperty("scene").GetInt32(),ms=a.GetProperty("message_slot").GetInt32();
            if(a.GetProperty("kind").GetString()!="set-scene-midi-message"||!seen.Add((s,ms))||!expected.Any(c=>c.Scene==s&&c.MessageSlot==ms))
                throw new InvalidDataException("Only exact existing-block message actions are permitted.");
            var am=a.GetProperty("message");
            if(am.GetProperty("type").GetString()!=type||am.GetProperty("channel").GetInt32()!=channel||am.GetProperty("value").GetInt32()!=value||
                (am.TryGetProperty("controller",out var ac)?ac.GetInt32():(int?)null)!=controller)
                throw new InvalidDataException("Action differs from requested message.");
            var before=a.GetProperty("before_message");var raw=before.GetProperty("raw");
            var actual=snapshot.Scenes[s-1].Messages[ms-1].Raw!;
            if(before.GetProperty("slot").GetInt32()!=ms||before.GetProperty("type").GetString()!="Off"||
                before.GetProperty("channel").ValueKind!=JsonValueKind.Null||before.GetProperty("value").ValueKind!=JsonValueKind.Null||
                new MidiRawFields(raw.GetProperty("type").GetInt32(),raw.GetProperty("channel").GetInt32(),raw.GetProperty("controller").GetInt32(),raw.GetProperty("value").GetInt32())!=actual)
                throw new InvalidDataException("Planner before-fields differ from the complete saved baseline.");
        }
        return plan;
    }
    public EditorCommand Command(int index)=>commands[index];
    public int[] ExpectedFields(int completedCommands)
    {
        if(completedCommands<0||completedCommands>CommandCount)throw new ArgumentOutOfRangeException(nameof(completedCommands));
        var result=originalFields.ToArray();foreach(var w in fields.Take(completedCommands))result[w.Parameter]=w.Value;return result;
    }
    internal byte[] BaselineWire()=>baseline.Sysex.ToArray();
    internal void VerifyBase(SavedCapture saved)
    {if(saved.Image.Slot!=Slot||PresetBody.Sha256(saved.Sysex)!=BaseSha256)throw new InvalidDataException("Scene MIDI saved base changed.");}
    internal void VerifyBase(PresetFileInspection live)
    {if(live.Model!=Device.Model||!live.RawImage.SequenceEqual(baseline.Image.RawImage)||!live.Body.SequenceEqual(baseline.Image.Body))throw new InvalidDataException("Unsaved work or loaded Scene MIDI base mismatch.");}
    public void VerifyProgress(StoredPresetImage changed,int completedCommands)
    {
        var expected=baseline.Image.Body.ToArray();var table=ExpectedFields(completedCommands);
        for(int i=0;i<table.Length;i++)BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(tableOffset+i*2,2),(ushort)table[i]);
        var snapshot=PresetBody.Decode(Device,changed);
        if(changed.Slot!=Slot||changed.Name!=baseline.Image.Name||!changed.SceneNames.SequenceEqual(baseline.Image.SceneNames)||
            snapshot.SceneMidiDecodeStatus!="decoded"||snapshot.Limitations.Length!=0||!changed.Body.SequenceEqual(expected)||
            !SceneMidiTable.SavedFields(Device,snapshot.BlockRecords).SequenceEqual(table))
            throw new InvalidDataException("Scene MIDI fields or unrelated complete body bytes differ from the exact plan.");
        EditorPreservation.VerifyRawRepresentation(baseline.Image,changed);
    }
    internal void VerifyProgress(PresetFileInspection live,int completedCommands)
    {if(live.Model!=Device.Model)throw new InvalidDataException("Wrong loaded model.");VerifyProgress(new StoredPresetImage(Slot,live.Name,live.SceneNames,live.RawImage,live.Body),completedCommands);}
}

public sealed class SceneMidiEditScope : EditorScope
{
    public SceneMidiEditPlan Plan { get; }
    public override DeviceDefinition Device=>Plan.Device;
    internal bool CanLoad,Prepared,BufferVerified,SavedVerified,Completed;
    internal int NextCommand;
    private int claimed;
    public SceneMidiEditScope(SceneMidiEditPlan plan)=>Plan=plan??throw new ArgumentNullException(nameof(plan));
    internal void ClaimSession(){if(Interlocked.Exchange(ref claimed,1)!=0)throw new InvalidOperationException("Scene MIDI scope cannot be reused.");}
    public override void Validate(EditorCommand c)
    {
        bool allowed=c.Operation switch
        {
            EditorOperation.ReloadPreset=>CanLoad||SavedVerified,
            EditorOperation.SetSceneMidiField=>Prepared&&NextCommand<Plan.CommandCount&&c.Frame.SequenceEqual(Plan.Command(NextCommand).Frame),
            EditorOperation.SavePreset=>BufferVerified&&!Plan.Unchanged,
            _=>false
        };
        if(c.Device!=Device||c.Slot!=Plan.Slot||!allowed)throw new InvalidOperationException("Command is outside the exact Scene MIDI edit scope.");
    }
}

/// <summary>Only a fully saved/reloaded result can produce an undo plan; its result hash is the required restore base.</summary>
public sealed class SceneMidiEditReceipt
{
    public SceneMidiEditPlan Plan { get; }
    public string ResultSha256 { get; }
    public PresetUploadPlan UndoUpload { get; }
    internal SceneMidiEditReceipt(SceneMidiEditPlan plan,SavedCapture result)
    {
        Plan=plan;ResultSha256=PresetBody.Sha256(result.Sysex);
        UndoUpload=PresetUploadPlan.Create(plan.Device,plan.Slot,plan.SavedScene,plan.BaselineWire(),plan.BaseSha256);
    }
    public void VerifyUndoBase(SavedCapture current)
    {if(current.Image.Slot!=Plan.Slot||PresetBody.Sha256(current.Sysex)!=ResultSha256)throw new InvalidDataException("Later saved edits conflict with backup-based undo.");}
}

public sealed class SceneMidiEditBatchScope : EditorScope
{
    private readonly SceneMidiEditScope[] scopes;
    private readonly HashSet<SceneMidiEditScope> used=[];
    private SceneMidiEditScope? active;
    public override DeviceDefinition Device { get; }
    public SceneMidiEditBatchScope(IEnumerable<SceneMidiEditScope> targets)
    {
        scopes=targets.ToArray();
        if(scopes.Length==0||scopes.Any(s=>s.Plan.Unchanged||s.Device!=scopes[0].Device)||scopes.Select(s=>s.Plan.Slot).Distinct().Count()!=scopes.Length)
            throw new ArgumentException("Scene MIDI batch needs distinct changed targets from one model.");
        Device=scopes[0].Device;
    }
    public void Activate(SceneMidiEditScope scope)
    {
        if(!scopes.Contains(scope)||used.Contains(scope)||active!=null&&!active.Completed)
            throw new InvalidOperationException("Only a completely verified Scene MIDI item can yield authorization; no replay.");
        used.Add(scope);active=scope;
    }
    public override void Validate(EditorCommand command)=>(active??throw new InvalidOperationException("No Scene MIDI item active.")).Validate(command);
}
