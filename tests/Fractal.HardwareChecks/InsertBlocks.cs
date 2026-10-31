using System.Text.Json;
using Fractal.Core;

// All targets/instances/cells/scene/hash choices come from an explicitly hash-bound caller plan.
static class InsertBlocks
{
    private sealed record Target(int Slot, int Row, int Column, int EffectId, int SavedScene, string SysexSha256);
    public static async Task<int> Run(string[] args)
    {
        var opts = new Dictionary<string,string>();
        for(int i=0;i<args.Length;i+=2)
        {
            if(i+1>=args.Length || args[i] is not ("--input" or "--output" or "--model" or "--capacity" or "--firmware" or "--destination" or "--plan" or "--expected-hash" or "--backup" or "--validate-only"))
                throw new ArgumentException("Invalid insertion options.");
            opts.Add(args[i],args[i+1]);
        }
        bool offline=opts.TryGetValue("--validate-only",out var validation)?validation switch{"true"=>true,"false"=>false,_=>throw new ArgumentException("Invalid --validate-only.")}:false;
        foreach(string key in offline?new[]{"--model","--firmware","--plan","--expected-hash","--backup"}:new[]{"--model","--firmware","--plan","--expected-hash","--backup","--input","--output","--destination"})
            if(!opts.ContainsKey(key))throw new ArgumentException($"Missing {key}.");
        var d=DeviceDefinition.Create(opts["--model"],opts.TryGetValue("--capacity",out var cap)?int.Parse(cap):null);
        byte[] planBytes=File.ReadAllBytes(opts["--plan"]);
        if(planBytes.Length>1024*1024 || !StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(planBytes),opts["--expected-hash"]))
            throw new InvalidDataException("Insertion request hash/size mismatch.");
        var targets=JsonSerializer.Deserialize<Target[]>(planBytes,SnapshotExport.JsonOptions) ?? throw new InvalidDataException("Empty request.");
        if(targets.Length==0 || targets.Length>d.PresetCapacity || targets.Select(t=>t.Slot).Distinct().Count()!=targets.Length)
            throw new InvalidDataException("Request needs distinct explicit targets within capacity.");
        string backup=Path.GetFullPath(opts["--backup"]);
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(backup,"manifest.json")));
        var plans=new List<BlockInsertionPlan>();
        foreach(var target in targets)
        {
            var entry=manifest.RootElement.GetProperty("captures").EnumerateArray().Single(e=>e.GetProperty("slot").GetInt32()==target.Slot);
            string file=entry.GetProperty("file").GetString()!;
            if(Path.GetFileName(file)!=file)throw new InvalidDataException("Capture path must be local.");
            if(target.SysexSha256!=entry.GetProperty("sysex_sha256").GetString())throw new InvalidDataException("Request/manifest hash mismatch.");
            byte[] wire=File.ReadAllBytes(Path.Combine(backup,file));
            var plan=BlockInsertionPlan.Create(d,wire,target.SysexSha256,target.Row,target.Column,target.EffectId,target.SavedScene);
            if(plan.Slot!=target.Slot)throw new InvalidDataException("Request/backup target mismatch.");
            plans.Add(plan);
        }
        if(offline){Console.WriteLine(SnapshotExport.Json(new{mode="offline-insertion-validation",targets=plans.Count,ports_opened=false}));return 0;}
        string folder=Path.GetFullPath(opts["--destination"]);
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Choose a new private destination.");
        Directory.CreateDirectory(folder);
        using var trace=new Trace(Path.Combine(folder,"trace.jsonl"));
        using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
        TimeSpan timeout=TimeSpan.FromSeconds(10);var results=new List<object>();string failure="",stage="preflight";int activeSlot=-1;
        try
        {
            var scopes=plans.Select(plan=>new BlockInsertionScope(plan)).ToArray();
            var batchScope=new BlockInsertionBatchScope(scopes);
            await using var midi=new TracedMidi(await TransportConnection.OpenAsync(int.Parse(opts["--input"]),int.Parse(opts["--output"]),batchScope),trace);
            DeviceContext context;
            await using(var identity=new ReadSession(new BorrowedMidi(midi)))
            {
                context=await identity.IdentifyAsync(d.Model,d.PresetCapacity,timeout,cancel.Token);
                if(context.Definition!=d || context.Firmware!=opts["--firmware"])throw new InvalidDataException("Exact model/firmware mismatch.");
            }
            foreach(var scope in scopes)
            {
                var plan=scope.Plan;
                activeSlot=plan.Slot;stage="fresh-base-and-safe-load";
                batchScope.Activate(scope);
                await using var session=new ReadSession(new BorrowedMidi(midi));
                session.AuthorizeBlockInsertion(scope);
                await session.PrepareBlockInsertionAsync(context,timeout,cancel.Token);
                string item=Path.Combine(folder,$"item-{results.Count+1:000}");Directory.CreateDirectory(item);
                stage="select-and-insert";
                await session.SendEditorAsync(EditorCommand.SelectCell(d,plan.Slot,plan.Row,plan.Column),timeout,cancel.Token);
                await session.SendEditorAsync(EditorCommand.InsertBlock(d,plan.Slot,plan.Row,plan.Column,plan.EffectId),timeout,cancel.Token);
                stage="verify-loaded-before-save";
                var live=await session.VerifyInsertedBufferAsync(context,timeout,cancel.Token);
                File.WriteAllBytes(Path.Combine(item,"inserted-buffer.private.syx"),live.Sysex);
                var status=await session.BlockStatusAsync(context,timeout,cancel.Token);
                if(status.Count(x=>x.EffectId==plan.EffectId)!=1)throw new InvalidDataException("Inserted instance absent or ambiguous in live status.");
                stage="save-once";await session.SendEditorAsync(EditorCommand.SavePreset(d,plan.Slot),timeout,cancel.Token);
                stage="complete-saved-verification";
                var saved=await session.VerifyInsertedSavedAsync(context,timeout,cancel.Token);
                SnapshotExport.Write(Path.Combine(item,"saved"),context,[saved]);
                var repeat=await session.VerifyInsertedSavedAsync(context,timeout,cancel.Token);
                if(!saved.Sysex.SequenceEqual(repeat.Sysex))throw new InvalidDataException("Repeated saved read differs.");
                stage="reload-and-verify";await session.SendEditorAsync(EditorCommand.ReloadPreset(d,plan.Slot),timeout,cancel.Token);
                await session.VerifyInsertedBufferAsync(context,timeout,cancel.Token);
                var reloaded=await session.VerifyInsertedSavedAsync(context,timeout,cancel.Token);
                if(!saved.Sysex.SequenceEqual(reloaded.Sysex))throw new InvalidDataException("Reloaded saved read differs.");
                SnapshotExport.Write(Path.Combine(item,"reloaded"),context,[reloaded]);
                var result=new {slot=plan.Slot,row=plan.Row,column=plan.Column,effect_id=plan.EffectId,saved_scene=plan.SavedScene,
                    original_hash=plan.BaseSha256,saved_hash=PresetBody.Sha256(saved.Sysex),raw_hash=PresetBody.Sha256(saved.Image.RawImage),body_hash=PresetBody.Sha256(saved.Image.Body),
                    complete=true,existing_body_and_scene_midi_preserved=true,raw_header_and_padding_preserved=true,repeated_saved_equal=true,reloaded_equal=true};
                File.WriteAllText(Path.Combine(item,"result.private.json"),SnapshotExport.Json(result));results.Add(result);
                Console.WriteLine($"Verified insertion {results.Count}/{plans.Count}; complete saved read and reload passed.");
                File.WriteAllText(Path.Combine(folder,"progress.private.json"),SnapshotExport.Json(new {complete=false,stage,results}));
            }
            stage="complete";
        }
        catch(Exception error){failure=$"{error.GetType().Name}: {error.Message}";}
        var report=new {protocol_release=ProtocolRelease.Version,model=d.Model,firmware=opts["--firmware"],complete=failure.Length==0,stage,failure,
            active_slot=activeSlot,requested_count=plans.Count,completed_count=results.Count,results,transmissions=trace.Sends,received_frames=trace.Receives,automatic_retry=false};
        File.WriteAllText(Path.Combine(folder,"report.private.json"),SnapshotExport.Json(report));
        Console.WriteLine(SnapshotExport.Json(new {complete=failure.Length==0,stage,failure,requested_count=plans.Count,completed_count=results.Count}));
        return failure.Length==0?0:1;
    }
    // Per-item sessions subscribe/unsubscribe normally; only the enclosing owner closes the native connection.
    private sealed class BorrowedMidi(IEditorTransport inner) : IEditorTransport
    {
        public event Action<byte[]>? Received { add=>inner.Received+=value; remove=>inner.Received-=value; }
        public event Action<Exception>? Faulted { add=>inner.Faulted+=value; remove=>inner.Faulted-=value; }
        public async Task SendReadAsync(byte[] request,CancellationToken token)
        {
            await Task.Delay(10,token); // query-spacing policy, not manufacturer timing
            await inner.SendReadAsync(request,token);
        }
        public Task SendEditorAsync(EditorCommand command,CancellationToken token)=>inner.SendEditorAsync(command,token);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
