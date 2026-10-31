using Fractal.Core;

static class ApplyPerformanceControl
{
    public static async Task<int> Run(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--request" or "--expected-hash" or "--validate-only" or "--input" or "--output" or "--destination")) throw new ArgumentException("Invalid performance control options.");
            options.Add(args[i], args[i + 1]);
        }
        string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing " + key);
        string file = Required("--request");
        if (new FileInfo(file).Length > 1_000_000) throw new InvalidDataException("Request exceeds bound.");
        byte[] bytes = File.ReadAllBytes(file);
        if (PresetBody.Sha256(bytes) != Required("--expected-hash")) throw new InvalidDataException("Request hash differs.");
        var scope = PerformanceControlScope.Parse(bytes);
        bool offline = options.GetValueOrDefault("--validate-only", "false") switch { "true" => true, "false" => false, _ => throw new ArgumentException("validate-only must be true or false.") };
        if (offline) { Console.WriteLine(SnapshotExport.Json(scope.Report())); return 0; }
        int input = int.Parse(Required("--input")), output = int.Parse(Required("--output")); string destination = Required("--destination");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new evidence directory.");
        using var cancellation = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var timeout = TimeSpan.FromSeconds(10);
        await using var session = new ReadSession(await TransportConnection.OpenAsync(input, output, scope));
        var context = await session.IdentifyAsync(scope.Device.Model, scope.Device.PresetCapacity, timeout, cancellation.Token);
        if (context.Firmware != scope.Firmware) throw new InvalidDataException("Firmware differs from reviewed performance context.");
        Console.WriteLine(SnapshotExport.Json(await scope.ExecuteAsync(session, context, destination, timeout, cancellation.Token))); return 0;
    }
}
