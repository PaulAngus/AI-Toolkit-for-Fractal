using Fractal.Core;
static class ApplyEditBatch
{
    public static async Task<int> Run(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--bundle" or "--expected-hash" or "--mode" or "--validate-only" or "--input" or "--output" or "--destination")) throw new ArgumentException("Invalid edit batch options.");
            options.Add(args[i], args[i + 1]);
        }
        string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing " + key);
        var batch = PresetEditBatch.Open(Required("--bundle"), Required("--expected-hash"));
        string mode = options.GetValueOrDefault("--mode", "apply"); if (mode is not ("apply" or "reconcile")) throw new ArgumentException("Mode must be apply or reconcile.");
        bool offline = options.GetValueOrDefault("--validate-only", "false") switch { "true" => true, "false" => false, _ => throw new ArgumentException("validate-only must be true or false.") };
        if (offline) { Console.WriteLine(SnapshotExport.Json(batch.Report())); return 0; }
        string destination = Required("--destination"); if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new evidence directory.");
        using var cancel = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        var timeout = TimeSpan.FromSeconds(10);
        await using var midi = await TransportConnection.OpenAsync(int.Parse(Required("--input")), int.Parse(Required("--output")), mode == "apply" ? batch.Scope : null);
        DeviceContext context;
        await using (var session = new ReadSession(new BorrowedPresetTransport(midi))) context = await session.IdentifyAsync(batch.Device.Model, batch.Device.PresetCapacity, timeout, cancel.Token);
        if (mode == "apply") Console.WriteLine(SnapshotExport.Json(await batch.RunAsync(midi, context, destination, timeout, cancel.Token)));
        else
        {
            Directory.CreateDirectory(destination); await using var reader = new ReadSession(new BorrowedPresetTransport(midi));
            var result = await batch.ReconcileAsync(reader, context, timeout, cancel.Token);
            File.WriteAllText(Path.Combine(destination, "reconciliation.json"), SnapshotExport.Json(result)); Console.WriteLine(SnapshotExport.Json(result));
        }
        return 0;
    }
}
