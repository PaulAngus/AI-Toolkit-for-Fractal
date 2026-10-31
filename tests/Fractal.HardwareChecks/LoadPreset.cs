using System.Text.Json;
using Fractal.Core;
static class LoadPreset
{
    public static async Task<int> Run(string[] args)
    {
        var opts=new Dictionary<string,string>();
        for(int i=0;i<args.Length;i+=2)
        {
            if(i+1>=args.Length||args[i] is not ("--request" or "--expected-hash" or "--input" or "--output" or "--destination" or "--validate-only"))throw new ArgumentException("Invalid explicit load options.");opts.Add(args[i],args[i+1]);
        }
        foreach(string key in new[]{"--request","--expected-hash"})if(!opts.ContainsKey(key))throw new ArgumentException($"Missing {key}.");
        bool offline=opts.TryGetValue("--validate-only",out var v)?v switch{"true"=>true,"false"=>false,_=>throw new ArgumentException("Invalid --validate-only.")}:false;
        if(new FileInfo(opts["--request"]).Length>1024*1024)throw new InvalidDataException("Load request exceeds bound.");
        byte[] request=File.ReadAllBytes(opts["--request"]);if(PresetBody.Sha256(request)!=opts["--expected-hash"])throw new InvalidDataException("Load request hash mismatch.");
        using var document=JsonDocument.Parse(request);var root=document.RootElement;
        if(root.GetProperty("schema_version").GetInt32()!=1)throw new InvalidDataException("Unsupported load schema.");
        var d=DeviceDefinition.Create(root.GetProperty("model").GetString()!,root.GetProperty("capacity").GetInt32());
        byte[] Capture(string key)
        {
            var c=root.GetProperty(key);string file=c.GetProperty("file").GetString()!;
            if(Path.GetFileName(file)!=file)throw new InvalidDataException("Load captures must be local.");
            byte[] wire=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(opts["--request"]))!,file));
            if(PresetBody.Sha256(wire)!=c.GetProperty("sha256").GetString())throw new InvalidDataException("Load capture hash mismatch.");return wire;
        }
        byte[] current=Capture("current_saved"),loaded=Capture("current_loaded"),target=Capture("target_saved");
        var before=new CurrentState(root.GetProperty("current_slot").GetInt32(),PresetFile.Inspect(d,loaded).Name,root.GetProperty("current_scene").GetInt32());
        var mapping = root.TryGetProperty("midi_mapping", out var mappingJson) && mappingJson.ValueKind != JsonValueKind.Null ? mappingJson.Deserialize<MidiPresetMapping>(LiveControlScope.JsonOptions) : null;
        var scope=new PresetLoadScope(d,root.GetProperty("firmware").GetString()!,before,current,loaded,target,root.GetProperty("discard_changes").GetBoolean(), mapping);
        if(scope.TargetSlot!=root.GetProperty("target_slot").GetInt32())throw new InvalidDataException("Load target mismatch.");
        if(offline){Console.WriteLine(SnapshotExport.Json(new{mode="offline-load-validation",midi_mapping=mapping,target_slot=scope.TargetSlot,target_scene=scope.TargetScene,wire_messages=scope.Commands.Select(c=>Convert.ToHexString(c.Frame)),ports_opened=false}));return 0;}
        foreach(string key in new[]{"--input","--output","--destination"})if(!opts.ContainsKey(key))throw new ArgumentException($"Missing {key}.");
        string folder=Path.GetFullPath(opts["--destination"]);if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Choose a new private load evidence directory.");
        Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"prior-saved.private.syx"),current);File.WriteAllBytes(Path.Combine(folder,"prior-loaded.private.syx"),loaded);File.WriteAllBytes(Path.Combine(folder,"target-saved.private.syx"),target);
        using var trace=new Trace(Path.Combine(folder,"trace.jsonl"));string failure="";
        using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};var token=cancel.Token;var timeout=TimeSpan.FromSeconds(10);
        try
        {
            await using var midi=new TracedMidi(await TransportConnection.OpenAsync(int.Parse(opts["--input"]),int.Parse(opts["--output"]),scope),trace);
            await using var session=new ReadSession(midi);var context=await session.IdentifyAsync(d.Model,d.PresetCapacity,timeout,token);
            session.AuthorizePresetLoad(scope);await session.PreparePresetLoadAsync(context,timeout,token);
            foreach (var command in scope.Commands) await session.SendEditorAsync(command,timeout,token);
            var result=await session.VerifyPresetLoadAsync(context,timeout,token);File.WriteAllBytes(Path.Combine(folder,"loaded-target.private.syx"),result.Sysex);
        }
        catch(Exception e){failure=$"{e.GetType().Name}: {e.Message}";}
        var report=new{protocol_release=ProtocolRelease.Version,model=d.Model,firmware=scope.Firmware,complete=failure.Length==0,failure,
            request_hash=PresetBody.Sha256(request),current_slot=before.Slot,target_slot=scope.TargetSlot,discard_changes=scope.DiscardChanges,
            original_loaded_buffer_retained=true,saved_images_verified_unchanged=failure.Length==0,transmissions=trace.Sends,received_frames=trace.Receives,
            automatic_retry=false};
        File.WriteAllText(Path.Combine(folder,"report.private.json"),SnapshotExport.Json(report));Console.WriteLine(SnapshotExport.Json(report));return failure.Length==0?0:1;
    }
}
