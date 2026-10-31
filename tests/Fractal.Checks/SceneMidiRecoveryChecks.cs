using Fractal.Core;

static class SceneMidiRecoveryChecks
{
    public static string DirectoryName()=>Path.Combine(Path.GetTempPath(),"fractal-recovery-"+Guid.NewGuid().ToString("N"));
    public static readonly string RequestHash=new('a',64);
    private static byte[] LoadedWire(SavedCapture saved)
    {byte[] wire=saved.Sysex.ToArray();wire[6]=127;wire[7]=0;wire[11]=SysexProtocol.ComputeChecksum(wire.AsSpan(0,11));return wire;}
    public static async Task<SceneMidiRecoveryOutcome> Run(SceneMidiRecoveryStore store,FakeEditorMidi midi,bool undo=false,Action<SceneMidiRecoveryStore>? checkpoint=null)
    {
        var authority=new SceneMidiRecoveryAuthority(store.Device);midi.Scope=authority;
        try{return await SceneMidiRecoveryRunner.RunAsync(store,midi,authority,undo,TimeSpan.FromSeconds(1),default,checkpoint);}
        finally{midi.Scope=null;}
    }
    private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Throws<T>(Action action) where T:Exception
    {try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
    private static async Task ThrowsAsync<T>(Func<Task> action) where T:Exception
    {try{await action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
    private static void Cleanup(string folder)
    {
        string full=Path.GetFullPath(folder),root=Path.GetFullPath(Path.GetTempPath());
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("fractal-recovery-"))throw new IOException("Unsafe test cleanup path.");
        if(Directory.Exists(full))Directory.Delete(full,true);
    }
    public static async Task RunAll(DeviceDefinition[] definitions,Func<string,Func<Task>,Task> check)
    {
        foreach(var d in definitions)
        await check($"{d.Model}/{d.PresetCapacity} explicit retained-buffer discard and one guarded preset load",async()=>
        {
            var b=new Batch(d);var f=b.Fixtures[0];var target=b.Fixtures[1];var body=f.Base.Image.Body.ToArray();body[3000]^=1;f.Live=Fixtures.FromBody(d,f.Plan.Slot,body);
            var before=new CurrentState(f.Plan.Slot,f.Live.Image.Name,3);
            Throws<InvalidDataException>(()=>new PresetLoadScope(d,"99.07",before,f.Base.Sysex,LoadedWire(f.Live),target.Base.Sysex,false));
            var scope=new PresetLoadScope(d,"99.07",before,f.Base.Sysex,LoadedWire(f.Live),target.Base.Sysex,true);
            b.Midi.Scope=scope;
            await using var s=new ReadSession(b.Midi);s.AuthorizePresetLoad(scope);await s.PreparePresetLoadAsync(f.Context,TimeSpan.FromSeconds(1),default);
            await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(d,target.Plan.Slot),TimeSpan.FromSeconds(1),default));
            foreach(var command in scope.Commands)await s.SendEditorAsync(command,TimeSpan.FromSeconds(1),default);
            await s.VerifyPresetLoadAsync(f.Context,TimeSpan.FromSeconds(1),default);
            await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(EditorCommand.ReloadPreset(d,target.Plan.Slot),TimeSpan.FromSeconds(1),default));
            Assert(b.Midi.Sent.Count(q=>q[5]==1)==1&&f.Saved==f.Base&&target.Saved==target.Base,"One Load, no Save or changed saved images");
        });
        await check("explicit load refuses changed loaded buffer, source selection or target after preparation",async()=>
        {
            foreach(string alteration in new[]{"live","target","selection"})
            {
                var b=new Batch(definitions[0]);var f=b.Fixtures[0];var target=b.Fixtures[1];
                var scope=new PresetLoadScope(f.Device,"99.07",new(f.Plan.Slot,f.Live.Image.Name,3),f.Base.Sysex,LoadedWire(f.Live),target.Base.Sysex,false);
                await using var s=new ReadSession(b.Midi);s.AuthorizePresetLoad(scope);await s.PreparePresetLoadAsync(f.Context,TimeSpan.FromSeconds(1),default);
                var body=(alteration=="live"?f.Base:target.Base).Image.Body.ToArray();body[3000]^=1;
                if(alteration=="live")f.Live=Fixtures.FromBody(f.Device,f.Plan.Slot,body);
                else if(alteration=="target")target.Saved=Fixtures.FromBody(target.Device,target.Plan.Slot,body);
                else f.ReportedScene++;
                await ThrowsAsync<InvalidDataException>(()=>s.SendEditorAsync(EditorCommand.ReloadPreset(f.Device,target.Plan.Slot),TimeSpan.FromSeconds(1),default));
                Assert(b.Midi.Sent.All(SysexProtocol.IsReadRequest),"No load when retained source/target changes");
            }
        });
        foreach(var d in definitions)
        await check($"{d.Model}/{d.PresetCapacity} durable two-target PC/CC resume, receipt import and batch undo",async()=>
        {
            string folder=DirectoryName();var batch=new Batch(d);string hash;
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,batch.Fixtures.Select(f=>f.Plan),RequestHash))
                {
                    var applied=await Run(store,batch.Midi);Assert(applied.Applied==2&&store.Items.All(i=>i.Status=="edited"),"Both target results retained");hash=store.StateHash;
                }
                int start=batch.Midi.Sent.Count;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))
                {
                    var outcome=await Run(store,batch.Midi);Assert(outcome.Applied==0&&outcome.VerifiedResults==2,"Completed edits verified without reapplying");
                    Assert(batch.Midi.Sent.Skip(start).All(q=>SysexProtocol.IsReadRequest(q)||q[5]==1&&q[6]==0x27),"Resume sends only guarded loads and reads");
                    foreach(var f in batch.Fixtures)store.Receipt(f.Plan.Slot).VerifyUndoBase(f.Saved);
                    hash=store.StateHash;
                }
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))
                {
                    var outcome=await Run(store,batch.Midi,undo:true);Assert(outcome.Undone==2&&store.Items.All(i=>i.Status=="undone"),"Two explicit guarded restores completed");hash=store.StateHash;
                    foreach(var f in batch.Fixtures)Assert(f.Saved.Sysex.SequenceEqual(f.Base.Sysex),"Full original bytes restored");
                }
                start=batch.Midi.Sent.Count;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))
                {
                    var outcome=await Run(store,batch.Midi);Assert(outcome.AlreadyTerminal==2,"Undone entries never reapplied by resume");
                    outcome=await Run(store,batch.Midi,undo:true);Assert(outcome.AlreadyTerminal==2,"Repeat undo is read-only");
                }
                Assert(batch.Midi.Sent.Skip(start).All(SysexProtocol.IsReadRequest),"Terminal repeat never navigates/writes");
            }
            finally{Cleanup(folder);}
        });
        await check("recovery rejects stale heads, concurrent owners and corrupt envelopes; ignores stale temporary file",()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);string old,hash;
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash))
                {
                    old=store.StateHash;Throws<IOException>(()=>SceneMidiRecoveryStore.Open(folder,old));store.Attempt(f.Plan.Slot,"load");hash=store.StateHash;
                }
                Throws<InvalidDataException>(()=>SceneMidiRecoveryStore.Open(folder,old));File.WriteAllText(Path.Combine(folder,"state.pending"),"interrupted data");
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))Assert(store.Items.Single().Status=="editing","Committed original survives stale temporary");
                byte[] good=File.ReadAllBytes(Path.Combine(folder,"state.private.json"));good[^3]^=1;File.WriteAllBytes(Path.Combine(folder,"state.private.json"),good);
                Throws<InvalidDataException>(()=>SceneMidiRecoveryStore.Open(folder,hash));
            }
            finally{Cleanup(folder);}return Task.CompletedTask;
        });
        await check("uncertain field stops the batch; unsaved partial state cannot be resumed or saved",async()=>
        {
            string folder=DirectoryName();var batch=new Batch(definitions[0]);string hash;var reply=batch.Midi.Reply;
            batch.Midi.Reply=(q,m)=>{reply!(q,m);if(q[5]==1&&q[6]==9)m.Error(new IOException("Synthetic field uncertainty"));};
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,batch.Fixtures.Select(f=>f.Plan),RequestHash))
                {
                    await ThrowsAsync<IOException>(()=>Run(store,batch.Midi));hash=store.StateHash;
                    Assert(store.Items.Single(i=>i.Slot==batch.Fixtures[1].Plan.Slot).Status=="pending","Second target untouched");
                }
                int start=batch.Midi.Sent.Count;batch.Midi.Reply=reply;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))await ThrowsAsync<InvalidDataException>(()=>Run(store,batch.Midi));
                Assert(batch.Midi.Sent.Skip(start).All(SysexProtocol.IsReadRequest),"Partial live data produces no reload/Save/replay");
                Assert(batch.Midi.Sent.All(q=>q[5]!=1||q[6]!=0x26),"No Save was attempted");
            }
            finally{Cleanup(folder);}
        });
        await check("uncertain Save is adopted only after full fresh verification; no Save/field replay",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);var reply=f.Midi.Reply;string hash;
            f.Midi.Reply=(q,m)=>{reply!(q,m);if(q[5]==1&&q[6]==0x26)m.Error(new IOException("Synthetic post-Save uncertainty"));};
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash))
                {await ThrowsAsync<IOException>(()=>Run(store,f.Midi));hash=store.StateHash;Throws<InvalidOperationException>(()=>store.Receipt(f.Plan.Slot));}
                int start=f.Midi.Sent.Count;f.Midi.Reply=reply;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))
                {
                    var result=await Run(store,f.Midi);Assert(result.VerifiedResults==1&&result.Applied==0,"Exact intended saved result reconciled");
                    store.Receipt(f.Plan.Slot).VerifyUndoBase(f.Saved);
                }
                Assert(f.Midi.Sent.Skip(start).All(q=>SysexProtocol.IsReadRequest(q)||q[5]==1&&q[6]==0x27),"Only safe verification loads, no repeated Save/fields");
            }
            finally{Cleanup(folder);}
        });
        await check("interruption before transmission permits fresh original reconciliation",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);string hash;
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash))
                {await ThrowsAsync<IOException>(()=>Run(store,f.Midi,checkpoint:_=>throw new IOException("Synthetic interruption before load")));hash=store.StateHash;}
                Assert(f.Midi.Sent.All(SysexProtocol.IsReadRequest),"Checkpoint persisted before transmission");
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))Assert((await Run(store,f.Midi)).Applied==1,"Verified unchanged original can be applied");
            }
            finally{Cleanup(folder);}
        });
        await check("later saved edits anywhere in a batch prevent its first write",async()=>
        {
            string folder=DirectoryName();var b=new Batch(definitions[0]);
            try
            {
                using var store=SceneMidiRecoveryStore.Create(folder,b.Fixtures.Select(f=>f.Plan),RequestHash);
                var last=b.Fixtures[1];var body=last.Base.Image.Body.ToArray();body[3000]^=1;last.Saved=last.Live=Fixtures.FromBody(last.Device,last.Plan.Slot,body);
                await ThrowsAsync<InvalidDataException>(()=>Run(store,b.Midi));Assert(b.Midi.Sent.All(SysexProtocol.IsReadRequest),"Whole batch conflicts before any write");
            }
            finally{Cleanup(folder);}
        });
        await check("imported receipts protect later saved and unsaved changes during undo",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);string hash;
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash)){await Run(store,f.Midi);hash=store.StateHash;}
                var result=f.Saved;var body=result.Image.Body.ToArray();body[3000]^=1;f.Saved=f.Live=Fixtures.FromBody(f.Device,f.Plan.Slot,body);int start=f.Midi.Sent.Count;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))await ThrowsAsync<InvalidDataException>(()=>Run(store,f.Midi,undo:true));
                Assert(f.Midi.Sent.Skip(start).All(SysexProtocol.IsReadRequest),"Later saved edit not overwritten");
                f.Saved=result;start=f.Midi.Sent.Count;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))await ThrowsAsync<InvalidDataException>(()=>Run(store,f.Midi,undo:true));
                Assert(f.Midi.Sent.Skip(start).All(SysexProtocol.IsReadRequest),"Unsaved change not discarded");
            }
            finally{Cleanup(folder);}
        });
        await check("uncertain undo Save restores original through verification without reupload or resave",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);string hash;var reply=f.Midi.Reply;
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash)){await Run(store,f.Midi);hash=store.StateHash;}
                f.Midi.Reply=(q,m)=>{reply!(q,m);if(q[5]==1&&q[6]==0x26)m.Error(new IOException("Synthetic post-undo-Save uncertainty"));};
                using(var store=SceneMidiRecoveryStore.Open(folder,hash)){await ThrowsAsync<IOException>(()=>Run(store,f.Midi,undo:true));hash=store.StateHash;}
                f.Midi.Reply=reply;int start=f.Midi.Sent.Count;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))
                {
                    await ThrowsAsync<InvalidDataException>(()=>Run(store,f.Midi));
                    Assert((await Run(store,f.Midi,undo:true)).Undone==1&&store.Items.Single().Status=="undone","Explicit undo reconciliation complete");
                }
                Assert(f.Midi.Sent.Skip(start).All(q=>SysexProtocol.IsReadRequest(q)||q[5]==1&&q[6]==0x27),"No repeated upload or Save");
            }
            finally{Cleanup(folder);}
        });
        await check("uncertain undo upload with partial live contents blocks automatic continuation",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);string hash;var reply=f.Midi.Reply;
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash)){await Run(store,f.Midi);hash=store.StateHash;}
                f.Midi.Reply=(q,m)=>{reply!(q,m);if(q[5]==0x79)m.Error(new IOException("Synthetic upload uncertainty"));};
                using(var store=SceneMidiRecoveryStore.Open(folder,hash)){await ThrowsAsync<IOException>(()=>Run(store,f.Midi,undo:true));hash=store.StateHash;}
                f.Midi.Reply=reply;int start=f.Midi.Sent.Count;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))await ThrowsAsync<InvalidDataException>(()=>Run(store,f.Midi,undo:true));
                Assert(f.Midi.Sent.Skip(start).All(SysexProtocol.IsReadRequest),"Unsaved original buffer not automatically saved or reuploaded");
            }
            finally{Cleanup(folder);}
        });
        await check("reconciliation rejects wrong firmware, independent table drift and unauthorized writes",async()=>
        {
            var f=new SceneMidiEditFixture(definitions[0]);var scope=new SceneMidiReconcileScope(f.Plan,true,f.Plan.BaseSha256);
            await using var session=new ReadSession(f.Midi);session.AuthorizeSceneMidiReconcile(scope);
            await ThrowsAsync<InvalidOperationException>(()=>session.VerifyRecoveredSceneMidiAsync(f.Context with{Firmware="wrong"},TimeSpan.FromSeconds(1),default));
            await ThrowsAsync<InvalidOperationException>(()=>session.SendEditorAsync(EditorCommand.SavePreset(f.Device,f.Plan.Slot),TimeSpan.FromSeconds(1),default));
            int reads=0;var reply=f.Midi.Reply;f.Midi.Reply=(q,m)=>{if(q[5]==0x1f&&++reads==2)f.ExtraWord++;reply!(q,m);};
            await ThrowsAsync<InvalidDataException>(()=>session.VerifyRecoveredSceneMidiAsync(f.Context,TimeSpan.FromSeconds(1),default));
            Assert(f.Midi.Sent.All(q=>SysexProtocol.IsReadRequest(q)||q[5]==1&&q[6]==0x27),"Only narrow loads permitted");
        });
        await check("receipt completion refuses corrupted preservation and differing reloads",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);
            try
            {
                using var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash);
                await using(var s=new ReadSession(f.Midi))await SceneMidiEditChecks.Apply(f,s,TimeSpan.FromSeconds(1));
                Throws<InvalidDataException>(()=>store.CompleteEdit(f.Plan.Slot,f.Saved.Sysex,f.Base.Sysex));
                var body=f.Saved.Image.Body.ToArray();body[3000]^=1;var bad=Fixtures.FromBody(f.Device,f.Plan.Slot,body);
                Throws<InvalidDataException>(()=>store.CompleteEdit(f.Plan.Slot,bad.Sysex,bad.Sysex));
                Throws<InvalidOperationException>(()=>store.Receipt(f.Plan.Slot));
            }
            finally{Cleanup(folder);}
        });
        await check("interrupted batch retains its completed first result and applies only the pending target",async()=>
        {
            string folder=DirectoryName();var b=new Batch(definitions[0]);string hash;
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,b.Fixtures.Select(f=>f.Plan),RequestHash))
                {
                    await ThrowsAsync<IOException>(()=>Run(store,b.Midi,checkpoint:s=>
                    {if(s.Items.Count(i=>i.Status=="edited")==1)throw new IOException("Synthetic completed-item interruption");}));
                    hash=store.StateHash;Assert(store.Items.Select(i=>i.Status).SequenceEqual(new[]{"edited","pending"}),"Completed/pending split retained");
                }
                int start=b.Midi.Sent.Count;
                using(var store=SceneMidiRecoveryStore.Open(folder,hash))
                {var r=await Run(store,b.Midi);Assert(r.VerifiedResults==1&&r.Applied==1,"One verified result and one fresh application");}
                Assert(b.Midi.Sent.Skip(start).Count(q=>q[5]==1&&q[6]==0x26)==1,"Completed target Save not repeated");
            }
            finally{Cleanup(folder);}
        });
        await check("recomputed outer hash cannot hide corrupt recovery payload or altered preserved result",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);
            try
            {
                using(var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash))await Run(store,f.Midi);
                string file=Path.Combine(folder,"state.private.json");byte[] good=File.ReadAllBytes(file);
                var envelope=System.Text.Json.Nodes.JsonNode.Parse(good)!;
                byte[] payload=Convert.FromBase64String(envelope["payload"]!.GetValue<string>());payload[^3]^=1;
                envelope["payload"]=Convert.ToBase64String(payload);
                byte[] bad=System.Text.Encoding.UTF8.GetBytes(envelope.ToJsonString());File.WriteAllBytes(file,bad);
                Throws<InvalidDataException>(()=>SceneMidiRecoveryStore.Open(folder,PresetBody.Sha256(bad)));
                envelope=System.Text.Json.Nodes.JsonNode.Parse(good)!;
                var data=System.Text.Json.Nodes.JsonNode.Parse(Convert.FromBase64String(envelope["payload"]!.GetValue<string>()))!;
                var body=f.Saved.Image.Body.ToArray();body[3000]^=1;var changed=Fixtures.FromBody(f.Device,f.Plan.Slot,body);var item=data["items"]![0]!;
                item["result"]=Convert.ToBase64String(changed.Sysex);item["reloaded"]=Convert.ToBase64String(changed.Sysex);item["result_hash"]=PresetBody.Sha256(changed.Sysex);
                payload=System.Text.Encoding.UTF8.GetBytes(data.ToJsonString());envelope["payload"]=Convert.ToBase64String(payload);envelope["payload_sha256"]=PresetBody.Sha256(payload);
                bad=System.Text.Encoding.UTF8.GetBytes(envelope.ToJsonString());File.WriteAllBytes(file,bad);
                Throws<InvalidDataException>(()=>SceneMidiRecoveryStore.Open(folder,PresetBody.Sha256(bad)));
            }
            finally{Cleanup(folder);}
        });
        await check("failed checkpoint storage stops before load and poisons the open record",async()=>
        {
            string folder=DirectoryName();var f=new SceneMidiEditFixture(definitions[0]);
            try
            {
                using var store=SceneMidiRecoveryStore.Create(folder,[f.Plan],RequestHash);
                Directory.CreateDirectory(Path.Combine(folder,"state.pending"));
                await ThrowsAsync<UnauthorizedAccessException>(()=>Run(store,f.Midi));
                Assert(f.Midi.Sent.All(SysexProtocol.IsReadRequest),"No load/field/Save if checkpoint cannot persist");
                Throws<IOException>(()=>store.Plan(f.Plan.Slot));
            }
            finally{Cleanup(folder);}
        });
    }
    public sealed class Batch
    {
        public SceneMidiEditFixture[] Fixtures { get; }
        public FakeEditorMidi Midi { get; }=new();
        private SceneMidiEditFixture selected;
        public Batch(DeviceDefinition d)
        {
            Fixtures=[new(d,d.PresetCapacity-1),new(d,d.PresetCapacity-2)];selected=Fixtures[0];
            Midi.Reply=(q,midi)=>
            {
                var target=selected;
                if(q[5]==3&&!(q[6]==127&&q[7]==127))target=Fixtures.Single(f=>f.Plan.Slot==(q[6]<<7|q[7]));
                if(q[5]==1&&q[6]==0x27)
                {
                    uint slot=0;for(int j=0;j<5;j++)slot|=(uint)q[12+j]<<(7*j);
                    selected=target=Fixtures.Single(f=>f.Plan.Slot==slot);
                }
                target.Midi.Reply!(q,midi);
            };
        }
        public void PartialUnsaved()=>selected.Live=Fixtures[0].Saved;
    }
}
