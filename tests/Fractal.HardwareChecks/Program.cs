using System.Diagnostics;
using System.Text.Json;
using Fractal.Core;

// Explicit opt-in only. This project is never run by ordinary CI.
if (args.Length == 0 || args[0] != "--hardware")
{
    Console.WriteLine("Hardware checks: --hardware [--scene-midi|--empty-write-pilot|--recover-empty-pilot|--restore-empty-pilot|--save-current] --input N --output N --model MODEL [--capacity 512|1024] --destination NEW_PRIVATE_DIRECTORY; pilot requires --preset SLOT --row R --column C --backup DIRECTORY --expected-hash SHA256; restore also requires --current-backup DIRECTORY --current-hash SHA256. Save-current instead requires --preset SLOT --scene SCENE --firmware VERSION --expected-channels ID:CHANNEL,... --backup FILE --expected-hash SHA256 --current-backup FILE --current-hash SHA256. See docs/save-current.md.");
    Console.WriteLine("Full restore: --hardware --restore-preset --input N --output N --model MODEL [--capacity N] --destination NEW_PRIVATE_DIRECTORY --preset SLOT --scene SCENE --firmware VERSION --backup FILE --expected-hash SHA256 --current-backup FILE --current-hash SHA256. See docs/preset-restore.md.");
    Console.WriteLine("Isolated insertion: --hardware --insert-blocks --input N --output N --model MODEL [--capacity N] --firmware VERSION --backup CAPTURE_DIRECTORY --plan REQUEST_JSON --expected-hash SHA256 --destination NEW_PRIVATE_DIRECTORY. See docs/block-insertion-execution.md.");
    Console.WriteLine("Existing Scene MIDI: --hardware --apply-scene-midi --input N --output N --model MODEL [--capacity N] --firmware VERSION --backup CAPTURE_DIRECTORY --plan PLANNER_JSON --expected-hash SHA256 --destination NEW_PRIVATE_DIRECTORY --undo-after true|false. See docs/scene-midi-execution.md.");
    Console.WriteLine("Saved Scene MIDI recovery: --hardware --scene-midi-recovery --mode inspect|resume|undo --recovery PRIVATE_DIRECTORY --expected-hash CURRENT_STATE_SHA256 [--validate-only true] [--input N --output N --destination NEW_PRIVATE_DIRECTORY]. See docs/scene-midi-recovery.md.");
    Console.WriteLine("Explicit preset load: --hardware --load-preset --request HASH_BOUND_JSON --expected-hash SHA256 [--validate-only true] [--input N --output N --destination NEW_PRIVATE_DIRECTORY]. See docs/preset-load.md.");
    return 2;
}
var options = new Dictionary<string, string>();
try
{
    TransportConnection.Configure(ref args, hardwareHelper: true);
    if (args.Length > 1 && args[1] == "--edit-batch") return await ApplyEditBatch.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--live-control") return await ApplyLiveControl.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--performance-control") return await ApplyPerformanceControl.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--apply-preset-edit") return await ApplyPresetEdit.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--insert-blocks") return await InsertBlocks.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--remove-blocks") return await RemoveBlocks.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--apply-scene-midi") return await ApplySceneMidi.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--scene-midi-recovery") return await RecoverSceneMidi.Run(args.Skip(2).ToArray());
    if (args.Length > 1 && args[1] == "--load-preset") return await LoadPreset.Run(args.Skip(2).ToArray());
    bool sceneMidiOnly = args.Length > 1 && args[1] == "--scene-midi";
    bool writePilot = args.Length > 1 && args[1] == "--empty-write-pilot";
    bool recoverPilot = args.Length > 1 && args[1] == "--recover-empty-pilot";
    bool restorePilot = args.Length > 1 && args[1] == "--restore-empty-pilot";
    bool saveCurrent = args.Length > 1 && args[1] == "--save-current";
    bool fullRestore = args.Length > 1 && args[1] == "--restore-preset";
    for (int i = sceneMidiOnly || writePilot || recoverPilot || restorePilot || saveCurrent || fullRestore ? 2 : 1; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] is not ("--input" or "--output" or "--model" or "--capacity" or "--destination" or "--preset" or "--row" or "--column" or "--backup" or "--expected-hash" or "--current-backup" or "--current-hash" or "--scene" or "--firmware" or "--expected-channels")) throw new ArgumentException("Invalid hardware-check options.");
        options.Add(args[i], args[i + 1]);
    }
    int input = int.Parse(options["--input"]), output = int.Parse(options["--output"]);
    string model = options["--model"];
    int? capacity = options.TryGetValue("--capacity", out string? cap) ? int.Parse(cap) : null;
    DeviceDefinition requested = DeviceDefinition.Create(model, capacity);
    bool anyPilot = writePilot || recoverPilot || restorePilot;
    if (fullRestore && new[] { "--preset", "--scene", "--firmware", "--backup", "--expected-hash", "--current-backup", "--current-hash" }.Any(k => !options.ContainsKey(k)))
        throw new ArgumentException("Restore-preset requires exact target, scene, firmware, backup and retained saved base with hashes.");
    if (fullRestore && options.Keys.Any(k => k is "--row" or "--column" or "--expected-channels"))
        throw new ArgumentException("Restore uses complete images rather than placement or partial channel expectations.");
    if (saveCurrent && new[] { "--preset", "--scene", "--firmware", "--backup", "--expected-hash", "--current-backup", "--current-hash", "--expected-channels" }.Any(k => !options.ContainsKey(k)))
        throw new ArgumentException("Save-current requires exact target, scene, firmware, expected channels, expected backup and saved base with hashes.");
    if (!saveCurrent && !fullRestore && options.Keys.Any(k => k is "--scene" or "--firmware" or "--expected-channels") || saveCurrent && options.Keys.Any(k => k is "--row" or "--column"))
        throw new ArgumentException("Options do not apply to the selected mode.");
    if (!anyPilot && !saveCurrent && !fullRestore && options.Keys.Any(k => k is "--preset" or "--row" or "--column" or "--backup" or "--expected-hash" or "--current-backup" or "--current-hash"))
        throw new ArgumentException("Pilot options require an explicit pilot mode.");
    if (anyPilot && new[] { "--preset", "--row", "--column", "--backup", "--expected-hash" }.Any(k => !options.ContainsKey(k)))
        throw new ArgumentException("Pilot requires target, placement, backup and expected base hash.");
    if (restorePilot && new[] { "--current-backup", "--current-hash" }.Any(k => !options.ContainsKey(k)))
        throw new ArgumentException("Restore requires retained current capture and hash.");
    if (!restorePilot && !saveCurrent && !fullRestore && options.Keys.Any(k => k is "--current-backup" or "--current-hash"))
        throw new ArgumentException("Current-capture options require --restore-empty-pilot.");
    string folder = Path.GetFullPath(options["--destination"]);
    if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Choose a new private evidence destination.");
    Directory.CreateDirectory(folder);
    if (fullRestore) return await RestorePreset.Run(input, output, requested, folder, options);
    if (saveCurrent) return await SaveCurrent.Run(input, output, requested, folder, options);
    if (writePilot || recoverPilot || restorePilot) return await EmptyWritePilot.Run(input, output, requested, folder, options, recoverPilot, restorePilot);
    if (sceneMidiOnly) return await SceneMidiChecks.Run(input, output, requested, folder);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var results = new List<object>();
    var durations = new List<double>();
    var captures = new List<SavedCapture>();
    var repeats = new List<object>();
    DeviceContext? context = null;
    CurrentState? before = null, after = null;
    ExportResult? exported = null;
    string? failure = null;
    int sends = 0;
    var elapsed = Stopwatch.StartNew();
    using var trace = new Trace(Path.Combine(folder, "trace.jsonl"));
    TimeSpan timeout = TimeSpan.FromSeconds(5);
    void Check(string name, bool passed) { results.Add(new { name, passed }); if (!passed) throw new InvalidDataException($"Hardware check failed: {name}"); }
    try
    {
        await using (var midi = new TracedMidi(await TransportConnection.OpenAsync(input, output), trace))
        await using (var session = new ReadSession(midi))
        {
            context = await session.IdentifyAsync(model, capacity, timeout, cancellation.Token);
            Check("identity-and-firmware", context.Definition == requested && context.Firmware.Length > 0);
            before = await session.CurrentAsync(context, timeout, cancellation.Token);
            var liveNames = new List<string>();
            for (int scene = 1; scene <= 8; scene++) liveNames.Add(await session.SceneNameAsync(context, scene, timeout, cancellation.Token));
            Check("live-scenes-coherent", before == await session.CurrentAsync(context, timeout, cancellation.Token));
            for (int slot = 0; slot < context.Definition.PresetCapacity; slot++)
            {
                var timer = Stopwatch.StartNew();
                captures.Add(await session.SavedAsync(context, slot, timeout, cancellation.Token));
                durations.Add(timer.Elapsed.TotalMilliseconds);
                if ((slot + 1) % 32 == 0)
                {
                    Console.WriteLine($"Read {slot + 1}/{context.Definition.PresetCapacity}; elapsed {elapsed.Elapsed.TotalSeconds:F1}s");
                    Check($"selection-after-{slot + 1}", before == await session.CurrentAsync(context, timeout, cancellation.Token));
                }
            }
            after = await session.CurrentAsync(context, timeout, cancellation.Token);
            Check("full-scan-state-preserved", before == after);
            var sampleSlots = new[] { 0, before.Slot, 127, 128, 255, 256, 383, 384, context.Definition.PresetCapacity - 1 }.Distinct().Order().ToArray();
            foreach (int slot in sampleSlots)
            {
                var repeat = await session.SavedAsync(context, slot, timeout, cancellation.Token);
                var original = captures.Single(c => c.Image.Slot == slot);
                bool identical = repeat.Sysex.SequenceEqual(original.Sysex) && repeat.Image.RawImage.SequenceEqual(original.Image.RawImage) && repeat.Image.Body.SequenceEqual(original.Image.Body);
                Check($"repeat-content-{slot}", identical);
                string name = await session.PresetNameAsync(context, slot, timeout, cancellation.Token);
                Check($"addressed-preset-name-{slot}", name == original.Image.Name);
                repeats.Add(new { slot, identical, content_sha256 = PresetBody.Sha256(repeat.Image.RawImage) });
            }
            after = await session.CurrentAsync(context, timeout, cancellation.Token);
            Check("sample-repeat-state-preserved", before == after);
            // Record the observed saved/live name relationship, without treating unsaved changes as an error.
            bool liveSavedNamesEqual = captures.Single(c => c.Image.Slot == before.Slot).Image.SceneNames.SequenceEqual(liveNames);
            results.Add(new { name = "live-saved-scene-name-comparison", observed_equal = liveSavedNamesEqual });
            exported = SnapshotExport.Write(Path.Combine(folder, "full-snapshot"), context, captures.ToArray(), before, after);
            Check("all-bodies-decoded", exported.DecodeErrors.Length == 0 && exported.DecodedPresets == requested.PresetCapacity);
            sends += midi.Sends;
        }
        // Physical open/send/receive/close; received replies deliberately suppressed to test failure behavior.
        foreach (bool cancelTest in new[] { false, true })
        {
            string testName = cancelTest ? "cancellation" : "timeout";
            await using var midi = new TracedMidi(await TransportConnection.OpenAsync(input, output), trace) { Suppress = true };
            await using var session = new ReadSession(midi);
            bool expectedFailure = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            if (cancelTest) deadline.CancelAfter(150);
            try { await session.IdentifyAsync(model, capacity, cancelTest ? timeout : TimeSpan.FromMilliseconds(250), deadline.Token); }
            catch (OperationCanceledException) when (cancelTest && !cancellation.IsCancellationRequested) { expectedFailure = true; }
            catch (TimeoutException) when (!cancelTest) { expectedFailure = true; }
            Check($"{testName}-observed", expectedFailure);
            Check($"{testName}-actual-reply-suppressed", midi.SuppressedPackets > 0);
            midi.DeliverHeld();
            bool poisoned = false;
            try { await session.IdentifyAsync(model, capacity, timeout, cancellation.Token); }
            catch (IOException) { poisoned = true; }
            Check($"{testName}-late-reply-rejected", poisoned && midi.Sends == 1);
            sends += midi.Sends;
        }
        await using (var midi = new TracedMidi(await TransportConnection.OpenAsync(input, output), trace))
        await using (var session = new ReadSession(midi))
        {
            DeviceContext reopened = await session.IdentifyAsync(model, capacity, timeout, cancellation.Token);
            CurrentState final = await session.CurrentAsync(reopened, timeout, cancellation.Token);
            Check("port-reopen-and-recovery", reopened == context && final == before);
            after = final; sends += midi.Sends;
        }
    }
    catch (Exception error)
    {
        failure = $"{error.GetType().Name}: {error.Message}";
        if (captures.Count > 0 && context != null && exported == null)
            exported = SnapshotExport.Write(Path.Combine(folder, "partial-snapshot"), context, captures.ToArray(), before, after);
    }
    elapsed.Stop();
    var report = new { protocol_release = ProtocolRelease.Version, started_utc = trace.Started,
        completed_utc = DateTimeOffset.UtcNow, device = context, state_before = before, state_after = after,
        complete = failure == null, failure, captured_presets = captures.Count, export = exported, results, repeats,
        total_sends = sends, trace_sends = trace.Sends, trace_receives = trace.Receives,
        all_transmissions_read_allowlisted = trace.AllReads, elapsed_seconds = elapsed.Elapsed.TotalSeconds,
        preset_read_ms_min = durations.Count > 0 ? durations.Min() : (double?)null,
        preset_read_ms_max = durations.Count > 0 ? durations.Max() : (double?)null,
        preset_read_ms_mean = durations.Count > 0 ? durations.Average() : (double?)null,
        limitations = new[] { "No navigation, writes or restore tested", "No physical cable removal or driver-hang test", "Semantic editor/front-panel comparison not performed", "Only connected model/firmware physically tested" } };
    File.WriteAllText(Path.Combine(folder, "report.private.json"), SnapshotExport.Json(report));
    Console.WriteLine(SnapshotExport.Json(new { complete = failure == null, failure, model, firmware = context?.Firmware,
        captured_presets = captures.Count, decoded_presets = exported?.DecodedPresets, state_preserved = before != null && before == after,
        transmissions = trace.Sends, all_transmissions_read_allowlisted = trace.AllReads, elapsed_seconds = elapsed.Elapsed.TotalSeconds }));
    return failure == null ? 0 : 1;
}
catch (Exception error) { Console.Error.WriteLine($"error: {error.Message}"); return 2; }

sealed class Trace : IDisposable
{
    private readonly StreamWriter writer;
    private readonly object gate = new();
    private readonly SysexAssembler assembler = new();
    public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
    public int Sends { get; private set; }
    public int Receives { get; private set; }
    public bool AllReads { get; private set; } = true;
    public Trace(string file) => writer = new StreamWriter(new FileStream(file, FileMode.CreateNew));
    public void Tx(byte[] frame)
    {
        lock (gate) { Sends++; AllReads &= SysexProtocol.IsReadRequest(frame); Write("tx", frame); }
    }
    public void Rx(byte[] bytes)
    {
        lock (gate) foreach (byte[] frame in assembler.Feed(bytes)) { Receives++; Write("rx", frame); }
    }
    private void Write(string direction, byte[] frame)
    {
        writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, direction,
            model = frame.Length > 4 ? (int?)frame[4] : null, function = frame.Length > 5 ? (int?)frame[5] : null,
            length = frame.Length, data_base64 = Convert.ToBase64String(frame) })); writer.Flush();
    }
    public void Dispose() { lock (gate) writer.Dispose(); }
}
sealed class TracedMidi : IPresetRestoreTransport
{
    private readonly IMidiTransport inner;
    private readonly Trace trace;
    private readonly List<byte[]> held = [];
    private readonly object gate = new();
    public event Action<byte[]>? Received;
    public event Action<Exception>? Faulted;
    public bool Suppress { get; set; }
    public int Sends { get; private set; }
    public int SuppressedPackets { get { lock (gate) return held.Count; } }
    public TracedMidi(IMidiTransport inner, Trace trace) { this.inner = inner; this.trace = trace; inner.Received += Receive; inner.Faulted += Error; }
    private void Receive(byte[] bytes)
    {
        trace.Rx(bytes);
        lock (gate) { if (Suppress) { if (held.Count >= 64) throw new IOException("Suppressed receive bound exceeded."); held.Add(bytes.ToArray()); return; } }
        Received?.Invoke(bytes);
    }
    private void Error(Exception error) => Faulted?.Invoke(error);
    public Task SendReadAsync(byte[] request, CancellationToken token) { trace.Tx(request); Sends++; return inner.SendReadAsync(request, token); }
    public Task SendEditorAsync(EditorCommand command, CancellationToken token)
    {
        if (inner is not IEditorTransport editor) throw new InvalidOperationException("No editor transport.");
        trace.Tx(command.Frame); Sends++; return editor.SendEditorAsync(command, token);
    }
    public void DeliverHeld() { byte[][] copy; lock (gate) { Suppress = false; copy = held.ToArray(); held.Clear(); } foreach (byte[] bytes in copy) Received?.Invoke(bytes); }
    public Task SendPresetFrameAsync(PresetUploadPlan plan, int index, CancellationToken token)
    {
        if (inner is not IPresetRestoreTransport restore) throw new InvalidOperationException("No preset restore transport.");
        trace.Tx(plan.Frame(index)); Sends++; return restore.SendPresetFrameAsync(plan, index, token);
    }
    public async ValueTask DisposeAsync() { inner.Received -= Receive; inner.Faulted -= Error; await inner.DisposeAsync(); }
}
