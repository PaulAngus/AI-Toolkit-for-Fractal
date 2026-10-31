using System.Text.Json;
namespace Fractal.Core;

public sealed record SceneMidiRecoveryItem(int Slot, string Status, string LastAttempt, string OriginalHash, string? ResultHash);

/// <summary>Private, hash-bound recovery data. Data import never authorizes device execution.</summary>
public sealed class SceneMidiRecoveryStore : IDisposable
{
    public const int SchemaVersion=1;
    private const int MaxBytes=64*1024*1024;
    private static readonly JsonSerializerOptions Options=new(SnapshotExport.JsonOptions)
    { UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private sealed record Item(int Slot, byte[] Original, string OriginalHash, SceneMidiMessageChange[] Changes,
        string Status, string LastAttempt, byte[]? Result, byte[]? Reloaded, string? ResultHash);
    private sealed record Data(int SchemaVersion, string ProtocolRelease, string RequestHash, string Model,
        int Capacity, string Firmware, long Generation, Item[] Items);
    private sealed record Envelope(byte[] Payload, string PayloadSha256);
    private Data data;
    private readonly string folder;
    private readonly FileStream lease;
    private bool disposed,failed;
    private readonly bool readOnlyPlanning;
    public DeviceDefinition Device=>DeviceDefinition.Create(data.Model,data.Capacity);
    public string Firmware=>data.Firmware;
    public string RequestHash=>data.RequestHash;
    public string StateHash { get; private set; }="";
    public long Generation=>data.Generation;
    public SceneMidiRecoveryItem[] Items=>data.Items.Select(i=>new SceneMidiRecoveryItem(i.Slot,i.Status,i.LastAttempt,i.OriginalHash,i.ResultHash)).ToArray();
    private SceneMidiRecoveryStore(string folder,FileStream lease,Data data,bool readOnlyPlanning=false)
    {this.folder=folder;this.lease=lease;this.data=data;this.readOnlyPlanning=readOnlyPlanning;}
    private static bool Hash(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private bool SupportedRelease()
    {
        if(data.ProtocolRelease==ProtocolRelease.Version)return true;
        // Schema-1 records began at 0.8.0. Older records can supply read-only
        // evidence or explicit offline migration; mutable import stays release-bound.
        return readOnlyPlanning&&Version.TryParse(data.ProtocolRelease,out var older)&&
            Version.TryParse(ProtocolRelease.Version,out var current)&&
            older.Build>=0&&older.ToString(3)==data.ProtocolRelease&&older.Major==0&&older.Minor>=8&&
            older<current;
    }
    public static SceneMidiRecoveryStore Create(string directory,IEnumerable<SceneMidiEditPlan> plans,string requestHash)
    {
        var p=plans.ToArray();
        if(!Hash(requestHash)||p.Length==0||p.Any(x=>x.Device!=p[0].Device||x.Firmware!=p[0].Firmware)||p.Select(x=>x.Slot).Distinct().Count()!=p.Length)
            throw new ArgumentException("Distinct plans from one device/firmware and an explicit lowercase request SHA-256 are required.");
        string folder=Path.GetFullPath(directory);
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Recovery destination already exists.");
        Directory.CreateDirectory(folder);
        var lease=new FileStream(Path.Combine(folder,"owner.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var state=new Data(SchemaVersion,ProtocolRelease.Version,requestHash,p[0].Device.Model,p[0].Device.PresetCapacity,p[0].Firmware,0,
            p.Select(x=>new Item(x.Slot,x.BaselineWire(),x.BaseSha256,x.Changes,x.Unchanged?"unchanged":"pending","none",null,null,null)).ToArray());
        var store=new SceneMidiRecoveryStore(folder,lease,state);
        try{store.Validate();store.Persist();return store;}catch{store.Dispose();throw;}
    }
    public static SceneMidiRecoveryStore Open(string directory,string expectedStateHash)
        =>OpenCore(directory,expectedStateHash,false);
    public static SceneMidiRecoveryStore OpenForSelectivePlanning(string directory,string expectedStateHash)
        =>OpenCore(directory,expectedStateHash,true);
    /// <summary>Copy settled and never-attempted schema-1 evidence into a new current-release store; never contacts a device.</summary>
    public static object Migrate(string source,string expectedStateHash,string destination)
    {
        using var old=OpenForSelectivePlanning(source,expectedStateHash);
        if(old.data.ProtocolRelease==ProtocolRelease.Version)
            throw new InvalidOperationException("Recovery record already uses the current release.");
        // A pending item with no recorded attempt has not entered the mutation lifecycle.
        // Attempt() checkpoints before sending; editing/undoing may have reached hardware
        // and must remain bound to the release that recorded that uncertainty.
        if(old.data.Items.Any(i=>i.Status is not ("edited" or "unchanged" or "undone") &&
            !(i.Status=="pending" && i.LastAttempt=="none" && i.Result==null && i.Reloaded==null && i.ResultHash==null)))
            throw new InvalidDataException("Only settled or never-attempted pending items can be migrated; retain interrupted records for exact-state reconciliation.");
        string target=Path.GetFullPath(destination);
        if(Directory.Exists(target)||File.Exists(target))throw new IOException("Recovery migration destination already exists.");
        Directory.CreateDirectory(target);
        var lease=new FileStream(Path.Combine(target,"owner.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        using var migrated=new SceneMidiRecoveryStore(target,lease,old.data with{ProtocolRelease=ProtocolRelease.Version});
        migrated.Validate();migrated.Persist();
        var provenance=new {source_protocol_release=old.data.ProtocolRelease,target_protocol_release=ProtocolRelease.Version,
            source_state_sha256=old.StateHash,target_state_sha256=migrated.StateHash,
            generation=old.data.Generation,item_count=old.data.Items.Length,
            edited_count=old.data.Items.Count(i=>i.Status=="edited"),
            unchanged_count=old.data.Items.Count(i=>i.Status=="unchanged"),
            undone_count=old.data.Items.Count(i=>i.Status=="undone"),
            pending_untouched_count=old.data.Items.Count(i=>i.Status=="pending"),
            original_unchanged=true,ports_opened=false,refreshed=false};
        byte[] receipt=System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(provenance));
        using(var file=new FileStream(Path.Combine(target,"migration.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {file.Write(receipt);file.Flush(true);}
        return provenance;
    }
    private static SceneMidiRecoveryStore OpenCore(string directory,string expectedStateHash,bool readOnlyPlanning)
    {
        if(!Hash(expectedStateHash))throw new ArgumentException("Supply the exact current recovery-state SHA-256.");
        string folder=Path.GetFullPath(directory);
        var lease=new FileStream(Path.Combine(folder,"owner.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        try
        {
            string file=Path.Combine(folder,"state.private.json");
            if(new FileInfo(file).Length>MaxBytes)throw new InvalidDataException("Recovery record exceeds size bound.");
            byte[] wire=File.ReadAllBytes(file);
            if(PresetBody.Sha256(wire)!=expectedStateHash)throw new InvalidDataException("Recovery state hash mismatch.");
            var envelope=JsonSerializer.Deserialize<Envelope>(wire,Options)??throw new InvalidDataException("Missing recovery envelope.");
            if(PresetBody.Sha256(envelope.Payload)!=envelope.PayloadSha256)throw new InvalidDataException("Recovery payload integrity mismatch.");
            var data=JsonSerializer.Deserialize<Data>(envelope.Payload,Options)??throw new InvalidDataException("Missing recovery data.");
            var store=new SceneMidiRecoveryStore(folder,lease,data,readOnlyPlanning){StateHash=expectedStateHash};store.Validate();return store;
        }
        catch{lease.Dispose();throw;}
    }
    private void Usable(){ObjectDisposedException.ThrowIf(disposed,this);if(failed)throw new IOException("Recovery store failed; reopen and reconcile before any device operation.");}
    private Item Find(int slot){Usable();return data.Items.Single(i=>i.Slot==slot);}
    public SceneMidiEditPlan Plan(int slot)
    {
        var i=Find(slot);var p=SceneMidiEditPlan.Create(Device,Firmware,i.Original,i.OriginalHash,i.Changes);
        if(p.Slot!=i.Slot)throw new InvalidDataException("Recovery target does not match addressed backup.");return p;
    }
    private void Validate()
    {
        if(data.SchemaVersion!=SchemaVersion||!SupportedRelease()||!Hash(data.RequestHash)||data.Generation<0||
            string.IsNullOrWhiteSpace(data.Firmware)||data.Items.Length is <1 or >1024||data.Items.Length>Device.PresetCapacity||
            data.Items.Select(i=>i.Slot).Distinct().Count()!=data.Items.Length)throw new InvalidDataException("Unsupported or invalid recovery contract.");
        foreach(var i in data.Items)
        {
            if(!Hash(i.OriginalHash)||i.Status is not ("pending" or "editing" or "edited" or "undoing" or "undone" or "unchanged")||
                i.LastAttempt is not ("none" or "load" or "field" or "save" or "reload" or "verify-result" or "undo-load" or "undo-upload" or "undo-save" or "undo-reload" or "verify-original"))
                throw new InvalidDataException("Invalid recovery item state.");
            var p=Plan(i.Slot);
            if((i.Status=="unchanged")!=p.Unchanged)throw new InvalidDataException("Recovery no-op status mismatch.");
            if(i.Result!=null||i.Reloaded!=null||i.ResultHash!=null)
            {
                if(i.Status is not ("edited" or "undoing" or "undone")||i.Result==null||i.Reloaded==null||!Hash(i.ResultHash))
                    throw new InvalidDataException("Incomplete result receipt.");
                _=ImportReceipt(i);
            }
            else if(i.Status is "edited" or "undoing" or "undone")throw new InvalidDataException("Completed edit needs complete saved/reloaded evidence.");
        }
    }
    private SceneMidiEditReceipt ImportReceipt(Item i)
    {
        var p=Plan(i.Slot);
        if(i.Result==null||i.Reloaded==null||PresetBody.Sha256(i.Result)!=i.ResultHash||!i.Result.SequenceEqual(i.Reloaded))
            throw new InvalidDataException("Saved/reloaded receipt integrity mismatch.");
        var saved=SnapshotExport.DecodeFile(Device,i.Result);p.VerifyProgress(saved.Image,p.CommandCount);
        return new(p,saved);
    }
    public SceneMidiEditReceipt Receipt(int slot)
    {var i=Find(slot);if(i.Status is not ("edited" or "undoing" or "undone"))throw new InvalidOperationException("No verified persistent receipt.");return ImportReceipt(i);}
    public (byte[] Original, byte[] Result) CompletedImagesForSelectiveUndo(int slot)
    {
        var i=Find(slot);
        if(i.Status!="edited")throw new InvalidOperationException("Selective undo requires a completed edit with no pending or terminal undo.");
        _=ImportReceipt(i);
        return (i.Original.ToArray(),i.Result!.ToArray());
    }
    public void Attempt(int slot,string operation)
    {
        var i=Find(slot);bool undo=operation is "undo-load" or "undo-upload" or "undo-save" or "undo-reload" or "verify-original";
        if(undo&&i.Status is not ("edited" or "undoing")||!undo&&i.Status is not ("pending" or "editing" or "edited"))
            throw new InvalidOperationException("Attempt is outside the recovery item lifecycle.");
        Update(i with{Status=undo?"undoing":i.Status=="edited"?"edited":"editing",LastAttempt=operation});
    }
    public void CompleteEdit(int slot,byte[] savedWire,byte[] reloadedWire)
    {
        var i=Find(slot);if(i.Status is not ("pending" or "editing" or "edited"))throw new InvalidOperationException("Cannot complete this edit.");
        var next=i with{Status="edited",Result=savedWire.ToArray(),Reloaded=reloadedWire.ToArray(),ResultHash=PresetBody.Sha256(savedWire)};
        _=ImportReceipt(next);
        if(i.ResultHash!=null&&i.ResultHash!=next.ResultHash)throw new InvalidDataException("Previously verified result changed.");
        Update(next);
    }
    public void CompleteUndo(int slot,byte[] savedWire,byte[] reloadedWire)
    {
        var i=Find(slot);if(i.Status!="undoing"||PresetBody.Sha256(savedWire)!=i.OriginalHash||!savedWire.SequenceEqual(reloadedWire))
            throw new InvalidDataException("Undo completion requires exact repeated/reloaded original bytes.");
        Update(i with{Status="undone"});
    }
    /// <summary>Saved-only reconciliation; navigation additionally requires fresh complete live/saved equality.</summary>
    public string Reconcile(int slot,SavedCapture current,bool undo)
    {
        var i=Find(slot);var p=Plan(slot);var actual=SnapshotExport.DecodeFile(Device,current.Sysex);
        if(actual.Image.Slot!=slot)throw new InvalidDataException("Wrong recovery target.");
        string hash=PresetBody.Sha256(current.Sysex);
        if(p.Unchanged)return hash==i.OriginalHash?"unchanged":"conflict";
        bool original=hash==i.OriginalHash;
        bool result=false;
        try{p.VerifyProgress(actual.Image,p.CommandCount);result=i.ResultHash==null||i.ResultHash==hash;}catch(InvalidDataException){}
        if(undo)
        {
            if(i.Status=="undone")return original?"undone":"conflict";
            if(i.Status=="undoing"&&original)return "verify-original";
            return i.Status is "edited" or "undoing"&&result?"undo":"conflict";
        }
        if(i.Status=="undone")return original?"undone":"conflict";
        if(i.Status=="undoing")return "conflict"; // Finish the explicit undo; never reapply it through resume.
        if(original&&i.Status is "pending" or "editing")return "apply";
        return result?"verify-result":"conflict";
    }
    private void Update(Item item)
    {
        Usable();if(readOnlyPlanning)throw new InvalidOperationException("Read-only recovery import cannot update retained state.");var old=data;
        data=data with{Generation=data.Generation+1,Items=data.Items.Select(i=>i.Slot==item.Slot?item:i).ToArray()};
        try{Validate();Persist();}catch{data=old;failed=true;throw;}
    }
    private void Persist()
    {
        byte[] payload=JsonSerializer.SerializeToUtf8Bytes(data,Options);
        byte[] wire=JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload,PresetBody.Sha256(payload)),Options);
        if(wire.Length>MaxBytes)throw new InvalidDataException("Recovery record exceeds size bound.");
        string temporary=Path.Combine(folder,"state.pending"),target=Path.Combine(folder,"state.private.json");
        // Flush before same-directory atomic replacement. A stale pending file is never imported.
        using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){file.Write(wire);file.Flush(true);}
        File.Move(temporary,target,true);StateHash=PresetBody.Sha256(wire);
    }
    public void Dispose(){if(disposed)return;disposed=true;lease.Dispose();}
}

/// <summary>One coordinated port owner; scopes still enforce each exact operation.</summary>
public sealed class SceneMidiRecoveryAuthority : EditorScope
{
    public override DeviceDefinition Device { get; }
    private EditorScope? active;
    private readonly HashSet<EditorScope> used=[];
    private bool yielded=true;
    public SceneMidiRecoveryAuthority(DeviceDefinition device)=>Device=device;
    public void Activate(EditorScope scope)
    {
        if(!yielded||scope.Device!=Device||scope is not (SceneMidiEditScope or SceneMidiReconcileScope or PresetRestoreScope)||!used.Add(scope))
            throw new InvalidOperationException("The previous recovery item must finish before a fresh scope is activated.");
        active=scope;yielded=false;
    }
    public void Complete(EditorScope scope)
    {
        bool complete=scope switch{SceneMidiEditScope s=>s.Completed,SceneMidiReconcileScope s=>s.Completed,PresetRestoreScope s=>s.Completed,_=>false};
        if(!ReferenceEquals(scope,active)||!complete)throw new InvalidOperationException("Recovery scope is not completely verified.");yielded=true;active=null;
    }
    public override void Validate(EditorCommand command)=>(active??throw new InvalidOperationException("No active recovery operation.")).Validate(command);
    public void ValidateUpload(PresetUploadPlan plan,int index)
    {if(active is not PresetRestoreScope s)throw new InvalidOperationException("No active recovery restore.");s.ValidateUpload(plan,index);}
}

/// <summary>Verify an already-saved original/result. Only a guarded target Load and Reload are permitted.</summary>
public sealed class SceneMidiReconcileScope : EditorScope
{
    public SceneMidiEditPlan Plan { get; }
    public bool Original { get; }
    public string ExpectedHash { get; }
    public override DeviceDefinition Device=>Plan.Device;
    internal bool CanLoad,Completed;
    private int claimed;
    public SceneMidiReconcileScope(SceneMidiEditPlan plan,bool original,string expectedHash){Plan=plan;Original=original;ExpectedHash=expectedHash;}
    internal void ClaimSession(){if(Interlocked.Exchange(ref claimed,1)!=0)throw new InvalidOperationException("Reconciliation scope cannot be reused.");}
    public override void Validate(EditorCommand c)
    {if(!CanLoad||c.Device!=Device||c.Slot!=Plan.Slot||c.Operation!=EditorOperation.ReloadPreset)throw new InvalidOperationException("Reconciliation permits only its verified target load/reload.");}
}
