using Fractal.Core;

static class RecoverSceneMidi
{
    public static async Task<int> Run(string[] args)
    {
        var o=new Dictionary<string,string>();
        for(int i=0;i<args.Length;i+=2)
        {
            if(i+1>=args.Length||args[i] is not ("--mode" or "--recovery" or "--expected-hash" or "--input" or "--output" or "--destination" or "--validate-only"))
                throw new ArgumentException("Invalid recovery options.");o.Add(args[i],args[i+1]);
        }
        foreach(string required in new[]{"--mode","--recovery","--expected-hash"})if(!o.ContainsKey(required))throw new ArgumentException($"Missing {required}.");
        if(o["--mode"] is not ("inspect" or "resume" or "undo"))throw new ArgumentException("Mode must be inspect, resume or undo.");
        bool offline=o["--mode"]=="inspect";
        if(o.TryGetValue("--validate-only",out var validate))offline|=validate switch{"true"=>true,"false"=>false,_=>throw new ArgumentException("--validate-only must be true or false.")};
        using var store=SceneMidiRecoveryStore.Open(o["--recovery"],o["--expected-hash"]);
        if(o["--mode"]=="undo"&&store.Items.Any(i=>i.Status is not ("edited" or "undoing" or "undone" or "unchanged")))
            throw new InvalidDataException("Resume and verify unfinished edits before requesting undo.");
        if(o["--mode"]=="resume"&&store.Items.Any(i=>i.Status=="undoing"))throw new InvalidDataException("Finish the explicit undo; resume cannot reapply an undoing item.");
        if(offline)
        {
            Console.WriteLine(SnapshotExport.Json(new{mode="offline-recovery-inspection",model=store.Device.Model,capacity=store.Device.PresetCapacity,firmware=store.Firmware,
                request_hash=store.RequestHash,state_hash=store.StateHash,generation=store.Generation,items=store.Items,ports_opened=false,fresh_hardware_reconciled=false}));return 0;
        }
        foreach(string required in new[]{"--input","--output","--destination"})if(!o.ContainsKey(required))throw new ArgumentException($"Missing {required}.");
        string folder=Path.GetFullPath(o["--destination"]);
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Choose a new private recovery evidence destination.");
        Directory.CreateDirectory(folder);using var trace=new Trace(Path.Combine(folder,"trace.jsonl"));
        using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
        string failure="";SceneMidiRecoveryOutcome? outcome=null;int otherVerified=0;var timeout=TimeSpan.FromSeconds(10);
        var authority=new SceneMidiRecoveryAuthority(store.Device);var token=cancel.Token;
        var initialItems=store.Items;
        try
        {
            await using var native=new TracedMidi(await TransportConnection.OpenAsync(int.Parse(o["--input"]),int.Parse(o["--output"]),authority),trace);
            await using var paced=new PacedMidi(native);
            async Task<SavedCapture[]> Inventory(string label)
            {
                await using var reads=new ReadSession(new PacedMidi(paced,pace:false));
                var context=await reads.IdentifyAsync(store.Device.Model,store.Device.PresetCapacity,timeout,token);
                if(context.Firmware!=store.Firmware)throw new InvalidDataException("Recovery inventory firmware mismatch.");
                var before=await reads.CurrentAsync(context,timeout,token);var captures=new List<SavedCapture>();
                for(int slot=0;slot<store.Device.PresetCapacity;slot++)
                {
                    captures.Add(await reads.SavedAsync(context,slot,timeout,token));
                    if((slot+1)%64==0)
                    {
                        Console.WriteLine($"{label}: verified {slot+1}/{store.Device.PresetCapacity} saved reads.");
                        if(before!=await reads.CurrentAsync(context,timeout,token))throw new InvalidDataException("Selection changed during recovery inventory.");
                    }
                }
                var after=await reads.CurrentAsync(context,timeout,token);
                if(before!=after)throw new InvalidDataException("Selection changed during recovery inventory.");
                SnapshotExport.Write(Path.Combine(folder,label),context,captures.ToArray(),before,after);return captures.ToArray();
            }
            var before=await Inventory("inventory-before");
            outcome=await SceneMidiRecoveryRunner.RunAsync(store,paced,authority,o["--mode"]=="undo",timeout,token,s=>
            {
                Console.WriteLine($"Recovery checkpoint {s.Generation} retained.");
                File.WriteAllText(Path.Combine(folder,"recovery-head.private.json"),SnapshotExport.Json(new{state_hash=s.StateHash,request_hash=s.RequestHash,generation=s.Generation}));
            });
            var after=await Inventory("inventory-after");var targets=store.Items.Select(i=>i.Slot).ToHashSet();
            foreach(var saved in before.Where(s=>!targets.Contains(s.Image.Slot)))
            {
                if(!saved.Sysex.SequenceEqual(after.Single(s=>s.Image.Slot==saved.Image.Slot).Sysex))throw new InvalidDataException("Non-target preset changed during recovery.");
                otherVerified++;
            }
            foreach(var i in store.Items)
                if(store.Reconcile(i.Slot,after.Single(s=>s.Image.Slot==i.Slot),o["--mode"]=="undo")=="conflict")
                    throw new InvalidDataException("Final target conflicts with its retained verified result.");
        }
        catch(Exception e){failure=$"{e.GetType().Name}: {e.Message}";}
        var report=new{protocol_release=ProtocolRelease.Version,complete=failure.Length==0,mode=o["--mode"],model=store.Device.Model,firmware=store.Firmware,
            request_hash=store.RequestHash,state_hash=store.StateHash,generation=store.Generation,initial_items=initialItems,items=store.Items,
            outcome,failure,other_saved_slots_verified=otherVerified,transmissions=trace.Sends,received_frames=trace.Receives,automatic_retry=false,automatic_rollback=false};
        File.WriteAllText(Path.Combine(folder,"report.private.json"),SnapshotExport.Json(report));Console.WriteLine(SnapshotExport.Json(report));
        return failure.Length==0?0:1;
    }
    private sealed class PacedMidi(IPresetRestoreTransport inner,bool pace=true):IPresetRestoreTransport
    {
        public event Action<byte[]>? Received{add=>inner.Received+=value;remove=>inner.Received-=value;}
        public event Action<Exception>? Faulted{add=>inner.Faulted+=value;remove=>inner.Faulted-=value;}
        public async Task SendReadAsync(byte[] q,CancellationToken t){if(pace)await Task.Delay(10,t);await inner.SendReadAsync(q,t);}
        public Task SendEditorAsync(EditorCommand c,CancellationToken t)=>inner.SendEditorAsync(c,t);
        public Task SendPresetFrameAsync(PresetUploadPlan p,int i,CancellationToken t)=>inner.SendPresetFrameAsync(p,i,t);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
