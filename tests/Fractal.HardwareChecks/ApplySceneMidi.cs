using System.Text.Json;
using Fractal.Core;

// Explicit, hash-bound planner execution. No port, target, message or block-placement defaults.
static class ApplySceneMidi
{
    public static async Task<int> Run(string[] args)
    {
        var opts=new Dictionary<string,string>();
        for(int i=0;i<args.Length;i+=2)
        {
            if(i+1>=args.Length||args[i] is not ("--input" or "--output" or "--model" or "--capacity" or "--firmware" or "--backup" or "--plan" or "--expected-hash" or "--destination" or "--undo-after" or "--validate-only"))
                throw new ArgumentException("Invalid Scene MIDI execution options.");
            opts.Add(args[i],args[i+1]);
        }
        bool validateOnly=opts.TryGetValue("--validate-only",out var validate)?validate switch{"true"=>true,"false"=>false,_=>throw new ArgumentException("--validate-only must be true or false.")}:false;
        var required=new[]{"--model","--firmware","--backup","--plan","--expected-hash","--undo-after"};
        foreach(string k in validateOnly?required:required.Concat(new[]{"--input","--output","--destination"}))
            if(!opts.ContainsKey(k))throw new ArgumentException($"Missing {k}.");
        bool undoAfter=opts["--undo-after"] switch{"true"=>true,"false"=>false,_=>throw new ArgumentException("--undo-after must be true or false.")};
        var d=DeviceDefinition.Create(opts["--model"],opts.TryGetValue("--capacity",out var c)?int.Parse(c):null);
        if(new FileInfo(opts["--plan"]).Length>1024*1024)throw new InvalidDataException("Scene MIDI request exceeds size bound.");
        byte[] request=File.ReadAllBytes(opts["--plan"]);
        if(!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(request),opts["--expected-hash"]))throw new InvalidDataException("Scene MIDI request hash mismatch.");
        using var document=JsonDocument.Parse(request);var root=document.RootElement;
        var targets=root.GetProperty("presets").EnumerateArray().ToArray();
        if(targets.Length==0||targets.Length>d.PresetCapacity||targets.Select(t=>t.GetProperty("slot").GetInt32()).Distinct().Count()!=targets.Length)
            throw new InvalidDataException("Choose distinct explicit Scene MIDI targets.");
        string backup=Path.GetFullPath(opts["--backup"]);
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(backup,"manifest.json")));
        string snapshotFile=manifest.RootElement.GetProperty("snapshot_file").GetString()!;
        if(Path.GetFileName(snapshotFile)!=snapshotFile||PresetBody.Sha256(File.ReadAllBytes(Path.Combine(backup,snapshotFile)))!=manifest.RootElement.GetProperty("snapshot_sha256").GetString())
            throw new InvalidDataException("Backup snapshot manifest mismatch.");
        var plans=new List<SceneMidiEditPlan>();
        foreach(var target in targets)
        {
            int slot=target.GetProperty("slot").GetInt32();d.ValidateSlot(slot);
            var entry=manifest.RootElement.GetProperty("captures").EnumerateArray().Single(e=>e.GetProperty("slot").GetInt32()==slot);
            string file=entry.GetProperty("file").GetString()!;
            if(Path.GetFileName(file)!=file)throw new InvalidDataException("Backup capture must be local.");
            byte[] wire=File.ReadAllBytes(Path.Combine(backup,file));var saved=SnapshotExport.DecodeFile(d,wire);
            if(saved.Image.Slot!=slot||PresetBody.Sha256(wire)!=entry.GetProperty("sysex_sha256").GetString()||
                PresetBody.Sha256(saved.Image.RawImage)!=entry.GetProperty("raw_sha256").GetString()||PresetBody.Sha256(saved.Image.Body)!=entry.GetProperty("body_sha256").GetString())
                throw new InvalidDataException("Backup capture manifest mismatch.");
            plans.Add(SceneMidiEditPlan.FromPlanner(d,opts["--firmware"],wire,PresetBody.Sha256(wire),root,slot));
        }
        if(undoAfter&&(plans.Count!=1||plans[0].Unchanged))throw new InvalidDataException("Undo-after qualification requires one changed explicit target.");
        if(validateOnly)
        {
            Console.WriteLine(SnapshotExport.Json(new{mode="offline-validation-only",model=d.Model,firmware=opts["--firmware"],
                targets=plans.Count,changed_targets=plans.Count(p=>!p.Unchanged),unchanged_targets=plans.Count(p=>p.Unchanged),
                field_commands=plans.Sum(p=>p.CommandCount),ports_opened=false}));return 0;
        }
        string folder=Path.GetFullPath(opts["--destination"]);
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Choose a new private destination.");
        Directory.CreateDirectory(folder);using var trace=new Trace(Path.Combine(folder,"trace.jsonl"));
        using var recovery=SceneMidiRecoveryStore.Create(Path.Combine(folder,"recovery"),plans,PresetBody.Sha256(request));
        using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
        var token=cancel.Token;var timeout=TimeSpan.FromSeconds(10);string stage="preflight",failure="";
        var results=new List<object>();int completed=0,unchanged=0,otherVerified=0;bool restored=false;
        SceneMidiEditReceipt? receipt=null;SavedCapture[]? beforeInventory=null;
        async Task<SavedCapture[]> Inventory(ReadSession s,DeviceContext context,string label)
        {
            stage=label;var before=await s.CurrentAsync(context,timeout,token);var captures=new List<SavedCapture>();
            for(int slot=0;slot<d.PresetCapacity;slot++)
            {
                captures.Add(await s.SavedAsync(context,slot,timeout,token));
                if((slot+1)%64==0)
                {
                    Console.WriteLine($"{label}: verified {slot+1}/{d.PresetCapacity} saved reads.");
                    if(before!=await s.CurrentAsync(context,timeout,token))throw new InvalidDataException("Selection changed during inventory.");
                }
            }
            var after=await s.CurrentAsync(context,timeout,token);
            if(before!=after)throw new InvalidDataException("Selection changed during inventory.");
            SnapshotExport.Write(Path.Combine(folder,label),context,captures.ToArray(),before,after);return captures.ToArray();
        }
        static bool Same(SavedCapture a,SavedCapture b)=>a.Sysex.SequenceEqual(b.Sysex)&&a.Image.RawImage.SequenceEqual(b.Image.RawImage)&&a.Image.Body.SequenceEqual(b.Image.Body);
        try
        {
            var scopes=plans.Where(p=>!p.Unchanged).Select(p=>new SceneMidiEditScope(p)).ToArray();
            EditorScope? batch=scopes.Length==0?null:new SceneMidiEditBatchScope(scopes);
            await using(var midi=new TracedMidi(await TransportConnection.OpenAsync(int.Parse(opts["--input"]),int.Parse(opts["--output"]),batch),trace))
            {
                DeviceContext context;
                await using(var reads=new ReadSession(new BorrowedMidi(midi)))
                {
                    context=await reads.IdentifyAsync(d.Model,d.PresetCapacity,timeout,token);
                    if(context.Definition!=d||context.Firmware!=opts["--firmware"])throw new InvalidDataException("Exact model/firmware mismatch.");
                    beforeInventory=await Inventory(reads,context,"inventory-before");
                    foreach(var p in plans.Where(p=>p.Unchanged))
                    {
                        if(PresetBody.Sha256((await reads.SavedAsync(context,p.Slot,timeout,token)).Sysex)!=p.BaseSha256)
                            throw new InvalidDataException("Already-correct saved base changed.");
                        unchanged++;
                    }
                }
                foreach(var scope in scopes)
                {
                    var p=scope.Plan;((SceneMidiEditBatchScope)batch!).Activate(scope);
                    string item=Path.Combine(folder,$"item-{completed+1:000}");Directory.CreateDirectory(item);
                    await using var session=new ReadSession(new BorrowedMidi(midi));session.AuthorizeSceneMidiEdit(scope);
                    stage="fresh-base-and-safe-load";recovery.Attempt(p.Slot,"load");await session.PrepareSceneMidiEditAsync(context,timeout,token);
                    for(int i=0;i<p.CommandCount;i++)
                    {
                        stage=$"field-{i+1}-of-{p.CommandCount}";
                        File.WriteAllText(Path.Combine(item,"progress.private.json"),SnapshotExport.Json(new{stage,slot=p.Slot,verified_fields=i}));
                        recovery.Attempt(p.Slot,"field");await session.SendEditorAsync(p.Command(i),timeout,token);await session.VerifySceneMidiFieldAsync(context,timeout,token);
                    }
                    stage="verify-complete-before-save";var loaded=await session.VerifySceneMidiBufferAsync(context,timeout,token);
                    File.WriteAllBytes(Path.Combine(item,"edited-buffer.private.syx"),loaded.Sysex);
                    stage="save-once";recovery.Attempt(p.Slot,"save");await session.SendEditorAsync(EditorCommand.SavePreset(d,p.Slot),timeout,token);
                    stage="verify-saved";var saved=await session.VerifySceneMidiSavedAsync(context,timeout,token);
                    SnapshotExport.Write(Path.Combine(item,"saved"),context,[saved]);await session.VerifySceneMidiSavedAsync(context,timeout,token);
                    stage="reload-and-verify";recovery.Attempt(p.Slot,"reload");await session.SendEditorAsync(EditorCommand.ReloadPreset(d,p.Slot),timeout,token);
                    await session.VerifySceneMidiBufferAsync(context,timeout,token);var reloaded=await session.VerifySceneMidiSavedAsync(context,timeout,token);
                    SnapshotExport.Write(Path.Combine(item,"reloaded"),context,[reloaded]);receipt=session.SceneMidiReceipt();
                    recovery.CompleteEdit(p.Slot,saved.Sysex,reloaded.Sysex);
                    var result=new{slot=p.Slot,original_hash=p.BaseSha256,result_hash=receipt.ResultSha256,field_commands=p.CommandCount,
                        complete=true,unrelated_body_and_default_preserved=true,raw_header_padding_preserved=true,repeated_saved_equal=true,reloaded_equal=true};
                    File.WriteAllText(Path.Combine(item,"result.private.json"),SnapshotExport.Json(result));results.Add(result);completed++;
                    Console.WriteLine($"Verified Scene MIDI edit {completed}/{scopes.Length}; saved and reload checks passed.");
                }
                await using var afterReads=new ReadSession(new BorrowedMidi(midi));var afterInventory=await Inventory(afterReads,context,"inventory-edited");
                var changed=plans.Where(p=>!p.Unchanged).Select(p=>p.Slot).ToHashSet();
                foreach(var original in beforeInventory.Where(b=>!changed.Contains(b.Image.Slot)))
                {if(!Same(original,afterInventory.Single(b=>b.Image.Slot==original.Image.Slot)))throw new InvalidDataException("Non-target saved preset changed.");otherVerified++;}
                foreach(var p in plans.Where(p=>!p.Unchanged))p.VerifyProgress(afterInventory.Single(c=>c.Image.Slot==p.Slot).Image,p.CommandCount);
            }
            if(undoAfter)
            {
                var r=receipt!;var restoreScope=new PresetRestoreScope(r.UndoUpload);
                await using var midi=new TracedMidi(await TransportConnection.OpenAsync(int.Parse(opts["--input"]),int.Parse(opts["--output"]),restoreScope),trace);
                await using var session=new ReadSession(new BorrowedMidi(midi));
                var context=await session.IdentifyAsync(d.Model,d.PresetCapacity,timeout,token);
                if(context.Firmware!=r.Plan.Firmware)throw new InvalidDataException("Undo firmware mismatch.");
                session.AuthorizePresetRestore(restoreScope);stage="undo-fresh-result-and-live-preflight";
                await session.PrepareSceneMidiUndoAsync(r,context,timeout,token);
                stage="undo-upload-once";recovery.Attempt(r.Plan.Slot,"undo-upload");await session.UploadPresetAsync(TimeSpan.FromSeconds(30),TimeSpan.FromMilliseconds(100),token);
                stage="undo-verify-loaded";var live=await session.VerifyUploadedPresetAsync(context,timeout,token);
                File.WriteAllBytes(Path.Combine(folder,"undo-buffer.private.syx"),live.Sysex);
                r.VerifyUndoBase(await session.SavedAsync(context,r.Plan.Slot,timeout,token));
                stage="undo-save-once";recovery.Attempt(r.Plan.Slot,"undo-save");await session.SendEditorAsync(EditorCommand.SavePreset(d,r.Plan.Slot),timeout,token);
                var saved=await session.VerifyRestoredSavedAsync(context,timeout,token);await session.VerifyRestoredSavedAsync(context,timeout,token);
                stage="undo-reload-and-verify";recovery.Attempt(r.Plan.Slot,"undo-reload");await session.SendEditorAsync(EditorCommand.ReloadPreset(d,r.Plan.Slot),timeout,token);
                await session.VerifyUploadedPresetAsync(context,timeout,token);var final=await session.VerifyRestoredSavedAsync(context,timeout,token);
                if(!Same(saved,final))throw new InvalidDataException("Undo saved repeat differs.");
                SnapshotExport.Write(Path.Combine(folder,"undo-reloaded"),context,[final]);
                recovery.CompleteUndo(r.Plan.Slot,saved.Sysex,final.Sysex);
                var inventory=await Inventory(session,context,"inventory-restored");
                foreach(var original in beforeInventory!)if(!Same(original,inventory.Single(c=>c.Image.Slot==original.Image.Slot)))
                    throw new InvalidDataException("Undo inventory does not exactly match the original.");
                restored=true;
            }
            stage="complete";
        }
        catch(Exception error){failure=$"{error.GetType().Name}: {error.Message}";}
        var report=new{protocol_release=ProtocolRelease.Version,model=d.Model,firmware=opts["--firmware"],complete=failure.Length==0,stage,failure,
            requested_targets=plans.Count,completed_targets=completed,unchanged_targets=unchanged,other_saved_slots_verified=otherVerified,
            undo_requested=undoAfter,original_complete_saved_inventory_restored=restored,results,transmissions=trace.Sends,received_frames=trace.Receives,
            automatic_retry=false,automatic_rollback=false};
        File.WriteAllText(Path.Combine(folder,"report.private.json"),SnapshotExport.Json(report));
        File.WriteAllText(Path.Combine(folder,"recovery-head.private.json"),SnapshotExport.Json(new{recovery_state_sha256=recovery.StateHash,recovery_request_sha256=recovery.RequestHash,recovery_generation=recovery.Generation}));
        Console.WriteLine(SnapshotExport.Json(new{complete=failure.Length==0,stage,failure,completed,unchanged,otherVerified,restored}));
        return failure.Length==0?0:1;
    }
    private sealed class BorrowedMidi(IPresetRestoreTransport inner):IPresetRestoreTransport
    {
        public event Action<byte[]>? Received{add=>inner.Received+=value;remove=>inner.Received-=value;}
        public event Action<Exception>? Faulted{add=>inner.Faulted+=value;remove=>inner.Faulted-=value;}
        public async Task SendReadAsync(byte[] q,CancellationToken t){await Task.Delay(10,t);await inner.SendReadAsync(q,t);}
        public Task SendEditorAsync(EditorCommand c,CancellationToken t)=>inner.SendEditorAsync(c,t);
        public Task SendPresetFrameAsync(PresetUploadPlan p,int i,CancellationToken t)=>inner.SendPresetFrameAsync(p,i,t);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
