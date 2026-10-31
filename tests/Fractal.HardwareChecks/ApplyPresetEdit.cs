using Fractal.Core;

static class ApplyPresetEdit
{
    public static async Task<int> Run(string[] args)
    {
        var opts = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--bundle" or "--expected-hash" or "--input" or "--output" or "--destination" or "--validate-only" or "--mode" or "--receipt" or "--receipt-hash"))
                throw new ArgumentException("Invalid preset edit options.");
            opts.Add(args[i], args[i + 1]);
        }
        string Required(string key) => opts.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing " + key);
        bool offline = opts.TryGetValue("--validate-only", out var check) ? check switch
        { "true" => true, "false" => false, _ => throw new ArgumentException("Use true or false for validate-only.") } : false;
        string mode = opts.GetValueOrDefault("--mode", "apply");
        if (mode is not ("apply" or "undo" or "reconcile")) throw new ArgumentException("Mode must be apply, undo or reconcile.");
        PresetEditPlan plan; PresetEditExecution execution;
        if (mode == "undo") (plan, execution) = PresetEditExecution.PrepareUndo(Required("--receipt"), Required("--receipt-hash"));
        else { plan = PresetEditPlan.OpenBundle(Required("--bundle"), Required("--expected-hash")); execution = new PresetEditExecution(plan); }
        if (offline) { Console.WriteLine(SnapshotExport.Json(new { mode = "offline-edit-validation", ports_opened = false, plan = plan.Report() })); return 0; }
        int input = int.Parse(Required("--input")), output = int.Parse(Required("--output")); string destination = Required("--destination");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new private execution directory.");
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var timeout = TimeSpan.FromSeconds(10);
        await using var midi = await TransportConnection.OpenAsync(input, output, mode == "reconcile" ? null : execution.Scope);
        await using var session = new ReadSession(midi);
        var context = await session.IdentifyAsync(plan.Device.Model, plan.Device.PresetCapacity, timeout, cancellation.Token);
        if (mode == "reconcile")
        {
            var reconciliation = await execution.ReconcileAsync(session, context, timeout, cancellation.Token, destination);
            File.WriteAllText(Path.Combine(destination, "reconciliation.json"), SnapshotExport.Json(reconciliation));
            Console.WriteLine(SnapshotExport.Json(reconciliation)); return 0;
        }
        var result = await execution.RunAsync(session, context, destination, timeout, cancellation.Token);
        Console.WriteLine(SnapshotExport.Json(result)); return 0;
    }
}
