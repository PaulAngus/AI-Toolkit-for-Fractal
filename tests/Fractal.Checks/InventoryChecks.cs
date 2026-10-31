using System.Text.Json;
using System.Text.Json.Nodes;
using Fractal.Core;

static class InventoryChecks
{
    public static async Task Run(DeviceDefinition d, string? output)
    {
        string root = Path.Combine(Path.GetTempPath(), "fractal-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var context = new DeviceContext(d, "99.07", d.Model == "Axe-Fx III" ? "explicit-user-capacity" : "model-definition");
        int last = d.PresetCapacity - 1;
        var fixtures = new Dictionary<int, SavedCapture> { [0] = Fixtures.Preset(d, 0), [last] = Fixtures.Preset(d, last) };
        TimeSpan timeout = TimeSpan.FromMilliseconds(250);
        var reads = new List<int>();
        int? failSlot = null; int selection = 0; bool drift = false; int stateQueries = 0;
        CancellationTokenSource? cancellation = null;
        FakeMidi Midi() => new()
        {
            Reply = (q, midi) =>
            {
                if (q[5] == 0x0d)
                {
                    if (drift && stateQueries++ > 0) selection = last;
                    byte[] payload = new byte[34]; payload[0] = (byte)(selection & 127); payload[1] = (byte)(selection >> 7);
                    midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0d, payload));
                }
                else if (q[5] == 0x0c) midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0c, 0));
                else if (q[5] == 3)
                {
                    int slot = q[6] << 7 | q[7]; reads.Add(slot);
                    if (slot == failSlot) { cancellation?.Cancel(); return; }
                    midi.Fragment(fixtures[slot].Sysex);
                }
                else throw new Exception("Unexpected inventory request.");
            }
        };
        JsonElement Inspect(PresetInventory store) => JsonSerializer.SerializeToElement(store.Inspect(), SnapshotExport.JsonOptions);
        try
        {
            string path = Path.Combine(root, "scan");
            using (var store = PresetInventory.Create(path, context, [last, 0]))
            {
                Reject<IOException>(() => PresetInventory.Open(path)); // One journal owner, including inspection.
                Check(Inspect(store).GetProperty("captured_slots").GetInt32() == 0, "New scan has no cached facts");
                Reject<InvalidOperationException>(() => store.Export(Path.Combine(root, "partial-export")));
                var midi = Midi(); await using var session = new ReadSession(midi);
                await RejectAsync<InvalidDataException>(() => store.RunAsync(session, context with { Firmware = "99.08" }, timeout, default));
                Check(midi.Sent.Count == 0, "Wrong context refused before queries");
                failSlot = last;
                await RejectAsync<TimeoutException>(() => store.RunAsync(session, context, timeout, default));
                Check(reads.SequenceEqual(new[] { 0, last }), "Failure has no automatic retry");
                var report = Inspect(store);
                Check(report.GetProperty("status").GetString() == "partial", "Partial read not complete");
                Check(report.GetProperty("entries")[1].GetProperty("status").GetString() == "failed", "Failed slot retained");
                Check(midi.Sent.All(SysexProtocol.IsReadRequest), "Only reads issued");
            }
            byte[] originalCheckpoint = File.ReadAllBytes(Path.Combine(path, "inventory.json"));
            string capturePath = Directory.GetFiles(path, "*.syx").Single();
            byte[] original = File.ReadAllBytes(capturePath);
            byte[] corrupt = original.ToArray(); corrupt[30] ^= 1; File.WriteAllBytes(capturePath, corrupt);
            Reject<InvalidDataException>(() => PresetInventory.Open(path));
            File.WriteAllBytes(capturePath, original);
            // Reject forged completion and filename/path injection before any device use.
            var node = JsonNode.Parse(originalCheckpoint)!;
            node["attempts"]![0]!["outcome"] = "complete";
            File.WriteAllText(Path.Combine(path, "inventory.json"), node.ToJsonString());
            Reject<InvalidDataException>(() => PresetInventory.Open(path));
            node = JsonNode.Parse(originalCheckpoint)!; node["captures"]![0]!["sysex_sha256"] = "../outside";
            File.WriteAllText(Path.Combine(path, "inventory.json"), node.ToJsonString());
            Reject<InvalidDataException>(() => PresetInventory.Open(path));
            File.WriteAllBytes(Path.Combine(path, "inventory.json"), originalCheckpoint);
            failSlot = null; reads.Clear();
            var old = fixtures[0]; byte[] changed = old.Image.Body.ToArray(); changed[3000] ^= 1;
            fixtures[0] = Fixtures.FromBody(d, 0, changed);
            using (var store = PresetInventory.Open(path))
            {
                await using var session = new ReadSession(Midi());
                await RejectAsync<InvalidDataException>(() => store.RunAsync(session, context, timeout, default));
                Check(reads.SequenceEqual(new[] { 0 }), "Changed baseline refuses missing reads");
                Check(Inspect(store).GetProperty("entries")[1].GetProperty("status").GetString() == "failed", "Earlier missing-slot failure survives later reconciliation failure");
                Check(File.ReadAllBytes(capturePath).SequenceEqual(original), "Changed device never replaces retained baseline");
            }
            fixtures[0] = old; reads.Clear();
            string? fixtureDestination = output == null ? null : Path.Combine(output, $"Inventory-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
            using (var store = PresetInventory.Open(path))
            {
                await using var session = new ReadSession(Midi());
                await store.RunAsync(session, context, timeout, default);
                Check(reads.SequenceEqual(new[] { 0, last }), "Resume revalidates retained slot then reads missing slot");
                Check(store.Complete && Inspect(store).GetProperty("scope").GetString() == "selected-slots", "Selected completion is not full-capacity coverage");
                var report = Inspect(store);
                Check(report.GetProperty("attempts").GetArrayLength() == 3 && report.GetProperty("attempts")[2].GetProperty("revalidated").GetInt32() == 1, "Negative attempts retained");
                await RejectAsync<InvalidOperationException>(() => store.RunAsync(session, context, timeout, default));
                var exported = store.Export(Path.Combine(root, "export"));
                Check(exported.DecodedPresets == 2 && exported.DecodeErrors.Length == 0, "Shared snapshot export");
                Check(File.ReadAllBytes(Path.Combine(root, "export", "preset-0000.syx")).SequenceEqual(original), "Export preserves capture bytes");
            }
            if (fixtureDestination != null)
            {
                Directory.CreateDirectory(fixtureDestination);
                foreach (string file in Directory.GetFiles(path).Where(f => !f.EndsWith(".lock", StringComparison.Ordinal)))
                    File.Copy(file, Path.Combine(fixtureDestination, Path.GetFileName(file)));
            }
            using (var reopened = PresetInventory.Open(path)) Check(reopened.Complete, "Completion survives process boundary");

            // Cancellation before a saved reply retains both the captured and unattempted scopes.
            using (var store = PresetInventory.Create(Path.Combine(root, "cancelled"), context, [0, last]))
            using (var cts = new CancellationTokenSource())
            {
                cancellation = cts; failSlot = 0; reads.Clear();
                await using var session = new ReadSession(Midi());
                await RejectAsync<OperationCanceledException>(() => store.RunAsync(session, context, timeout, cts.Token));
                var report = Inspect(store);
                Check(report.GetProperty("entries")[0].GetProperty("status").GetString() == "cancelled" &&
                    report.GetProperty("entries")[1].GetProperty("status").GetString() == "unattempted", "Cancelled versus unattempted is explicit");
            }
            cancellation = null; failSlot = null;
            // Simulate death after a capture-file flush but before its checkpoint reference.
            string cancelledPath = Path.Combine(root, "cancelled");
            var interrupted = JsonNode.Parse(File.ReadAllText(Path.Combine(cancelledPath, "inventory.json")))!;
            interrupted["attempts"]![0]!["outcome"] = "started";
            interrupted["attempts"]![0]!["ended_utc"] = null;
            interrupted["attempts"]![0]!["error"] = null;
            File.WriteAllText(Path.Combine(cancelledPath, "inventory.json"), interrupted.ToJsonString());
            File.WriteAllBytes(Path.Combine(cancelledPath, $"preset-0000-{PresetBody.Sha256(fixtures[0].Sysex)}.syx"), fixtures[0].Sysex);
            using (var store = PresetInventory.Open(cancelledPath))
            {
                Check(Inspect(store).GetProperty("entries")[0].GetProperty("status").GetString() == "interrupted", "Started attempt exposes interruption");
                Check(Inspect(store).GetProperty("captured_slots").GetInt32() == 0, "Unreferenced orphan is not scan progress");
                reads.Clear(); await using var session = new ReadSession(Midi());
                await store.RunAsync(session, context, timeout, default);
                Check(reads.SequenceEqual(new[] { 0, last }) && store.Complete, "Orphan accepted only after fresh matching capture");
            }
            using (var store = PresetInventory.Create(Path.Combine(root, "drift"), context, [0]))
            {
                drift = true; stateQueries = 0; selection = 0;
                await using (var session = new ReadSession(Midi()))
                    await RejectAsync<InvalidDataException>(() => store.RunAsync(session, context, timeout, default));
                Check(!store.Complete && Inspect(store).GetProperty("captured_slots").GetInt32() == 1, "All captures with selection drift still partial");
                drift = false;
                await using var fresh = new ReadSession(Midi());
                await store.RunAsync(fresh, context, timeout, default);
                Check(store.Complete, "Fresh stable reconciliation completes retained scan");
            }
            fixtures[0] = Fixtures.Preset(d, 0, unknownAmpLayout: true);
            using (var store = PresetInventory.Create(Path.Combine(root, "unknown-body"), context, [0]))
            {
                await using var session = new ReadSession(Midi());
                await store.RunAsync(session, context, timeout, default);
                Check(store.Complete && !Inspect(store).GetProperty("decode_complete").GetBoolean(), "Integrity completion distinct from semantic decoding");
                Check(store.Export(Path.Combine(root, "unknown-export")).DecodeErrors.Length == 1, "Unknown bodies retained as export errors");
            }
            Reject<ArgumentException>(() => PresetInventory.Create(Path.Combine(root, "duplicate"), context, [0, 0]));
            Reject<ArgumentOutOfRangeException>(() => PresetInventory.Create(Path.Combine(root, "bounds"), context, [d.PresetCapacity]));
            Reject<ArgumentException>(() => PresetInventory.Create(Path.Combine(root, "empty"), context, []));
            Reject<ArgumentException>(() => PresetInventory.Create(Path.Combine(root, "bad-firmware"), context with { Firmware = "99.7" }, [0]));
            using (var store = PresetInventory.Create(Path.Combine(root, "full-scope"), context, Enumerable.Range(0, d.PresetCapacity)))
            {
                var report = Inspect(store);
                Check(report.GetProperty("scope").GetString() == "full-capacity" && report.GetProperty("requested_slots").GetInt32() == d.PresetCapacity && !store.Complete,
                    "Full capacity is retained explicitly without inventing successful reads");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
