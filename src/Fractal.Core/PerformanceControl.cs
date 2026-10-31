using System.Text.Json;
using System.Text.Json.Serialization;
namespace Fractal.Core;

public sealed record LooperState(int Flags)
{
    public bool Recording => (Flags & 1) != 0;
    public bool Playing => (Flags & 2) != 0;
    public bool Overdubbing => (Flags & 4) != 0;
    public bool Once => (Flags & 8) != 0;
    public bool Reverse => (Flags & 16) != 0;
    public bool HalfSpeed => (Flags & 32) != 0;
}
public sealed record PerformanceControlContext(int SchemaVersion, LiveControlContext Preset, LooperState? Looper);
public sealed record PerformanceControlRequest(int SchemaVersion, PerformanceControlContext Context, string Kind,
    int? Bpm = null, bool? Enabled = null, string? Action = null);

/// <summary>Published Axe-Fx III forms; model-addressed FM3/FM9 adaptation is experimental, not hardware qualification.</summary>
public static class PerformanceProtocol
{
    public static void ValidateTempo(DeviceDefinition device, int bpm)
    {
        // Each model's own owner manual documents the same whole-number range.
        var range = device.Model switch { "FM9" => (20, 250), "FM3" => (20, 250), "Axe-Fx III" => (20, 250), _ => throw new ArgumentException("Unsupported model.") };
        if (bpm < range.Item1 || bpm > range.Item2) throw new ArgumentOutOfRangeException(nameof(bpm), "Tempo must be a whole number from 20 to 250 BPM.");
    }
    internal static byte Button(string action) => action switch
    { "record" => 0, "play" => 1, "undo" => 2, "once" => 3, "reverse" => 4, "half-speed" => 5, _ => throw new ArgumentException("Unknown looper button.") };
    internal static int StateBit(string action) => action switch
    { "play" => 2, "reverse" => 16, "half-speed" => 32, _ => throw new ArgumentException("Absolute looper state supports play, reverse or half-speed.") };
    public static byte[] LooperQuery(DeviceDefinition device) => SysexProtocol.Frame(device.ModelByte, 0x0f, 127);
    public static LooperState DecodeLooper(DeviceDefinition device, byte[] frame)
    {
        if (!SysexProtocol.ValidFrame(frame, device.ModelByte, 0x0f, 9) || frame[6] > 63) throw new InvalidDataException("Invalid looper state reply.");
        return new(frame[6]);
    }
    public static int DecodeTempo(DeviceDefinition device, byte[] frame)
    {
        if (!SysexProtocol.ValidFrame(frame, device.ModelByte, 0x14, 10)) throw new InvalidDataException("Invalid tempo reply.");
        int bpm = frame[6] | frame[7] << 7;
        try { ValidateTempo(device, bpm); } catch (ArgumentOutOfRangeException e) { throw new InvalidDataException("Tempo reply is outside documented bounds (or is a query echo).", e); }
        return bpm;
    }
    public static object Capabilities(DeviceDefinition device) => new
    {
        model = device.Model, capacity = device.PresetCapacity, minimum_bpm = 20, maximum_bpm = 250,
        kinds = new[] { "tempo-read", "tempo-set", "tempo-tap", "tuner-set", "looper-button", "looper-state" },
        looper_buttons = new[] { "record", "play", "undo", "once", "reverse", "half-speed" },
        absolute_looper_states = new[] { "play", "reverse", "half-speed" },
        ports_opened = false,
        tempo_query_requires_review = true, tuner_state_query_supported = false, hardware_qualified = false,
        protocol_evidence = device.Model == "Axe-Fx III" ? "manufacturer MIDI specification; 1.13+ specification scope" : "experimental model-addressed adaptation; tempo query independently bound in pinned Gen-3 codec; other command forms inferred",
        tempo_query_limit = "Upstream reports query-as-set on unspecified early Axe-Fx III firmware; never included in ordinary read snapshots.",
        persistence = "No Save. Tempo can update global tempo and current unsaved preset tempo; unmapped loaded changes require reconciliation.",
        notification_limit = "Manufacturer tempo/tuner push specification describes MIDI Out, not USB; pushes do not confirm requested state."
    };
}

/// <summary>One capture-bound performance operation. Absolute requests and single button events have distinct contracts.</summary>
public sealed class PerformanceControlScope : EditorScope
{
    public static readonly JsonSerializerOptions JsonOptions = new(SnapshotExport.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly PerformanceControlRequest request;
    private readonly EditorCommand? command;
    private int claimed, queries;
    internal bool Ready, Attempted;
    public override DeviceDefinition Device { get; }
    public string Firmware => request.Context.Preset.Firmware;
    public int Slot => request.Context.Preset.Current.Slot;
    internal int QueryLimit => request.Kind == "tempo-read" ? 1 : request.Kind == "tempo-set" ? 2 : 0;
    public bool Unchanged { get; private set; }
    public static PerformanceControlScope Parse(byte[] bytes)
    {
        if (bytes.Length > 1_000_000) throw new InvalidDataException("Performance request exceeds bound.");
        try { return new(JsonSerializer.Deserialize<PerformanceControlRequest>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing performance request.")); }
        catch (JsonException e) { throw new InvalidDataException("Invalid performance control JSON.", e); }
    }
    public PerformanceControlScope(PerformanceControlRequest input)
    {
        request = JsonSerializer.Deserialize<PerformanceControlRequest>(JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions), JsonOptions)!;
        var c = request.Context ?? throw new ArgumentException("Retained performance context required.");
        if (c.Preset == null || request.SchemaVersion != 1 || c.SchemaVersion != 1 || c.Looper?.Flags is < 0 or > 63) throw new ArgumentException("Invalid performance context.");
        // Reuse the complete saved/temporary capture validation without granting its scene command.
        Device = new LiveControlScope(new(1, c.Preset, "scene", Scene: c.Preset.Current?.Scene)).Device;
        bool hasLooper = c.Preset.Blocks.Any(b => BlockCatalogue.Resolve(Device.Model, b.EffectId).Family == "Looper");
        if (hasLooper != (c.Looper != null)) throw new ArgumentException("Looper observation must match a present model-qualified block.");
        if (request.Kind == "tempo-set" && request.Bpm != null && request.Enabled == null && request.Action == null)
            command = EditorCommand.SetTempo(Device, Slot, request.Bpm.Value);
        else if (request.Kind is "tempo-read" or "tempo-tap" && request.Bpm == null && request.Enabled == null && request.Action == null)
            command = request.Kind == "tempo-tap" ? EditorCommand.TapTempo(Device, Slot) : null;
        else if (request.Kind == "tuner-set" && request.Bpm == null && request.Enabled != null && request.Action == null)
            command = EditorCommand.SetTuner(Device, Slot, request.Enabled.Value);
        else if (request.Kind is "looper-button" or "looper-state" && request.Bpm == null && request.Action != null && c.Looper != null)
        {
            command = EditorCommand.LooperButton(Device, Slot, request.Action);
            if (request.Kind == "looper-state" && request.Enabled != null)
                Unchanged = ((c.Looper.Flags & PerformanceProtocol.StateBit(request.Action)) != 0) == request.Enabled;
            else if (request.Kind != "looper-button" || request.Enabled != null) throw new ArgumentException("Choose a single button event or an explicit supported looper state.");
        }
        else throw new ArgumentException("Invalid performance operation or unrelated fields.");
    }
    public object Report() => new
    {
        model = Device.Model, capacity = Device.PresetCapacity, firmware = Firmware, slot = Slot,
        request.Kind, request.Bpm, request.Enabled, request.Action, unchanged = Unchanged,
        event_count = request.Kind is "tempo-tap" or "looper-button" ? 1 : 0,
        capabilities = PerformanceProtocol.Capabilities(Device), automatic_retry = false, ports_opened = false,
        hardware_qualified = false, persistence = "performance-only-no-save",
        state_verification = request.Kind is "tuner-set" or "tempo-tap" or "looper-button" ? "sent-unverified; observed looper flags do not verify audio/undo/button effects" : "bounded queried state",
        saved_sha256 = PresetBody.Sha256(Convert.FromBase64String(request.Context.Preset.SavedCapture)),
        loaded_sha256 = PresetBody.Sha256(Convert.FromBase64String(request.Context.Preset.LoadedCapture))
    };
    internal void Claim() { if (Interlocked.Exchange(ref claimed, 1) != 0) throw new InvalidOperationException("Performance scope cannot be reused."); }
    public override void Validate(EditorCommand c)
    {
        bool query = c.Operation == EditorOperation.QueryTempo;
        byte[]? expected = query ? EditorCommand.QueryTempo(Device, Slot).Frame : command?.Frame;
        if (!Ready || c.Device != Device || c.Slot != Slot || expected == null || !c.Frame.SequenceEqual(expected) || query && QueryLimit == 0)
            throw new InvalidOperationException("Command differs from the exact performance scope or was already attempted.");
    }
    internal void ConsumeQuery()
    {
        Validate(EditorCommand.QueryTempo(Device, Slot));
        if (queries >= QueryLimit) throw new InvalidOperationException("Reviewed tempo query budget exhausted; no replay.");
        queries++;
    }
    internal async Task CheckBefore(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        if (Attempted || context.Definition != Device || context.Firmware != Firmware) throw new InvalidDataException("Performance context differs or command was already attempted.");
        var fresh = await CaptureAsync(session, context, timeout, token);
        if (SnapshotExport.Json(fresh) != SnapshotExport.Json(request.Context)) throw new InvalidDataException("Performance context changed; prepare a fresh review.");
    }
    public static async Task<PerformanceControlContext> CaptureAsync(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var preset = await LiveControlScope.CaptureAsync(session, context, timeout, token);
        bool present = preset.Blocks.Any(b => BlockCatalogue.Resolve(context.Definition.Model, b.EffectId).Family == "Looper");
        var looper = present ? await session.LooperAsync(context, timeout, token) : null;
        if (preset.Current != await session.CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection drift during performance capture.");
        return new(1, preset, looper);
    }
    private void VerifyPreset(PerformanceControlContext after)
    {
        if (SnapshotExport.Json(after.Preset) != SnapshotExport.Json(request.Context.Preset))
            throw new InvalidDataException("Saved or loaded preset, selection or block status changed. Tempo may change an unmapped unsaved field; reconcile retained captures without replay.");
    }
    public async Task<object> ExecuteAsync(ReadSession session, DeviceContext context, string destination, TimeSpan timeout, CancellationToken token)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new performance evidence directory.");
        Directory.CreateDirectory(destination); string phase = "preflight";
        void Checkpoint(string state, string? error = null)
        {
            using var stream = new FileStream(Path.Combine(destination, "attempt.json"), FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.Write(JsonSerializer.SerializeToUtf8Bytes(new { state, phase, error, automatic_retry = false }, JsonOptions)); stream.Flush(true);
        }
        File.WriteAllBytes(Path.Combine(destination, "request.json"), JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)); Checkpoint("started");
        try
        {
            session.AuthorizePerformanceControl(this); await CheckBefore(session, context, timeout, token); Ready = true;
            int? beforeBpm = null, afterBpm = null;
            if (request.Kind is "tempo-read" or "tempo-set")
            {
                phase = "explicit-tempo-query"; Checkpoint("started");
                beforeBpm = await session.ReviewedTempoAsync(context, Slot, timeout, token);
                File.WriteAllText(Path.Combine(destination, "tempo-before.json"), SnapshotExport.Json(new { bpm = beforeBpm }));
                // A legacy query can itself change tempo. Do not proceed to a set if captures changed.
                var queried = await CaptureAsync(session, context, timeout, token);
                File.WriteAllText(Path.Combine(destination, "after-query.json"), SnapshotExport.Json(queried)); VerifyPreset(queried);
                Unchanged = request.Kind == "tempo-set" && beforeBpm == request.Bpm;
            }
            if (command != null && !Unchanged)
            { phase = "send-once"; Checkpoint("started"); await session.SendEditorAsync(command, timeout, token); }
            phase = "observe"; Checkpoint("started");
            if (request.Kind == "tempo-set" && !Unchanged) afterBpm = await session.ReviewedTempoAsync(context, Slot, timeout, token);
            else afterBpm = beforeBpm;
            var after = await CaptureAsync(session, context, timeout, token);
            File.WriteAllText(Path.Combine(destination, "observed.json"), SnapshotExport.Json(new { bpm = afterBpm, context = after }));
            VerifyPreset(after);
            if (request.Kind == "tempo-set" && afterBpm != request.Bpm) throw new InvalidDataException("Tempo readback did not match the requested BPM.");
            if (request.Kind == "looper-state")
            {
                int bit = PerformanceProtocol.StateBit(request.Action!);
                if (after.Looper == null || ((after.Looper.Flags & bit) != 0) != request.Enabled || (after.Looper.Flags & ~bit) != (request.Context.Looper!.Flags & ~bit))
                    throw new InvalidDataException("Looper state did not confirm exactly the requested flag.");
            }
            bool verified = request.Kind is "tempo-read" or "tempo-set" or "looper-state";
            phase = verified ? "verified-state" : "sent-unverified"; Checkpoint("complete");
            var result = new { complete = true, phase, state_verified = verified, unchanged = Unchanged, command_sent = Attempted,
                bpm = afterBpm, looper = after.Looper, saved_unchanged = true, loaded_unchanged = true, automatic_retry = false, hardware_qualified = false };
            File.WriteAllText(Path.Combine(destination, "result.json"), SnapshotExport.Json(result)); return result;
        }
        catch (Exception e) { Checkpoint("stopped-uncertain", e.Message); throw; }
        finally { Ready = false; }
    }
}
public sealed partial class ReadSession
{
    public void AuthorizePerformanceControl(PerformanceControlScope scope) => AuthorizeEditor(scope);
    public Task<LooperState> LooperAsync(DeviceContext context, TimeSpan timeout, CancellationToken token) =>
        Exchange(PerformanceProtocol.LooperQuery(context.Definition), frame =>
        {
            if (frame.Length < 6 || frame[4] != context.Definition.ModelByte || frame[5] != 0x0f) return (false, new LooperState(0));
            return (true, PerformanceProtocol.DecodeLooper(context.Definition, frame));
        }, timeout, token);
    internal Task<int> ReviewedTempoAsync(DeviceContext context, int slot, TimeSpan timeout, CancellationToken token)
    {
        var query = EditorCommand.QueryTempo(context.Definition, slot);
        return Exchange(query.Frame, frame =>
        {
            if (frame.Length < 6 || frame[4] != context.Definition.ModelByte || frame[5] != 0x14) return (false, 0);
            return (true, PerformanceProtocol.DecodeTempo(context.Definition, frame));
        }, timeout, token, query);
    }
}
