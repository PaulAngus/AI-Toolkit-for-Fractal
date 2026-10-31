using System.Text.Json;
using System.Text.Json.Serialization;
namespace Fractal.Core;

public sealed record LiveControlContext(int SchemaVersion, string Model, int Capacity, string Firmware,
    CurrentState Current, BlockStatus[] Blocks, string SavedCapture, string LoadedCapture);
public sealed record LiveControlRequest(int SchemaVersion, LiveControlContext Context, string Kind,
    int? EffectId = null, string? Channel = null, bool? Bypassed = null, int? Scene = null, ConfiguredCc? MidiAssignment = null);

/// <summary>One absolute live control, bound to retained saved/unsaved state. Never Save or replay.</summary>
public sealed class LiveControlScope : EditorScope
{
    public static readonly JsonSerializerOptions JsonOptions = new(SnapshotExport.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public override DeviceDefinition Device { get; }
    private readonly LiveControlRequest request;
    private readonly byte[] saved, loaded;
    private readonly PresetFileInspection baseline;
    private readonly EditorCommand command;
    private readonly HashSet<int> permittedBodyBytes = [];
    private int? stateWord;
    internal bool Ready, Attempted;
    private int claimed;
    public string Firmware => request.Context.Firmware;
    public int Slot => request.Context.Current.Slot;
    public bool Unchanged { get; }
    internal EditorCommand Command => command;
    public static LiveControlScope Parse(byte[] bytes)
    {
        if (bytes.Length > 1_000_000) throw new InvalidDataException("Live control request exceeds bound.");
        try { return new(JsonSerializer.Deserialize<LiveControlRequest>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing live request.")); }
        catch (JsonException e) { throw new InvalidDataException("Invalid live control JSON.", e); }
    }
    public LiveControlScope(LiveControlRequest input)
    {
        request = JsonSerializer.Deserialize<LiveControlRequest>(JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions), JsonOptions)!;
        var c = request.Context ?? throw new ArgumentException("Retained control context required.");
        Device = DeviceDefinition.Create(c.Model, c.Capacity); _ = EditReadCatalogue.Select(Device, c.Firmware);
        if (request.SchemaVersion != 1 || c.SchemaVersion != 1 || c.Current == null || c.Current.Scene is < 1 or > 8 || c.Blocks == null ||
            c.Blocks.Length > 100 || c.Blocks.Any(b => b == null || b.EffectId is < 1 or > 1000 || b.ChannelCount is < 1 or > 4 || b.Channel < 1 || b.Channel > b.ChannelCount) || c.Blocks.Select(b => b.EffectId).Distinct().Count() != c.Blocks.Length) throw new ArgumentException("Invalid live control context.");
        Device.ValidateSlot(Slot); saved = Convert.FromBase64String(c.SavedCapture); loaded = Convert.FromBase64String(c.LoadedCapture);
        var s = SnapshotExport.DecodeFile(Device, saved); baseline = PresetFile.Inspect(Device, loaded);
        if (s.Image.Slot != Slot || baseline.HeaderPayload[0] != 127 || baseline.HeaderPayload[1] is not (0 or 127) || baseline.Name != c.Current.Name)
            throw new InvalidDataException("Context needs an addressed saved capture and the current temporary edit buffer.");
        if (request.Kind == "scene")
        {
            if (request.Scene is null or < 1 or > 8 || request.EffectId != null || request.Channel != null || request.Bypassed != null) throw new ArgumentException("Scene control takes only scene 1–8.");
            command = EditorCommand.SelectScene(Device, Slot, request.Scene.Value); Unchanged = c.Current.Scene == request.Scene;
            permittedBodyBytes.UnionWith([0, 1]);
        }
        else
        {
            if (request.Scene != null || request.EffectId is null or < 1 or > 1000) throw new ArgumentException("Block control needs an exact instance.");
            var block = c.Blocks.SingleOrDefault(b => b.EffectId == request.EffectId) ?? throw new InvalidDataException("Requested block is absent from live status.");
            int channel = request.Channel is "A" or "B" or "C" or "D" ? request.Channel[0] - 'A' + 1 : 0;
            if (request.Kind == "channel" && request.Bypassed == null && channel > 0 && channel <= block.ChannelCount)
            { command = EditorCommand.SetBlockChannel(Device, Slot, block.EffectId, channel); Unchanged = block.Channel == channel; }
            else if (request.Kind == "bypass" && request.Channel == null && request.Bypassed != null)
            { command = EditorCommand.SetBlockBypass(Device, Slot, block.EffectId, request.Bypassed.Value); Unchanged = block.Bypassed == request.Bypassed; }
            else throw new ArgumentException("Choose absolute channel A–D within the reported count, or an explicit bypassed Boolean.");
            byte[] addressed = loaded.ToArray(); addressed[6] = (byte)(Slot >> 7); addressed[7] = (byte)(Slot & 127);
            addressed[11] = SysexProtocol.ComputeChecksum(addressed.AsSpan(0, 11));
            var projection = PresetEditProjection.Decode(Device, Firmware, addressed);
            var record = projection.Blocks.SingleOrDefault(b => b.EffectId == block.EffectId)?.Record;
            if (record != null && record.Rows == 4)
            {
                int offset = record.ShapeOffset + (request.Kind == "channel" ? c.Current.Scene - 2 : c.Current.Scene + 6) * 2;
                permittedBodyBytes.UnionWith([offset, offset + 1]);
                stateWord = offset;
            }
            // Unmapped bodies remain usable when the command changes only effective live status.
        }
        if (request.MidiAssignment != null) command = EditorCommand.SendCc(Device, Slot, request.MidiAssignment);
    }
    public object Report() => new { model = Device.Model, capacity = Device.PresetCapacity, firmware = Firmware, slot = Slot,
        current_scene = request.Context.Current.Scene, request.Kind, request.EffectId, request.Channel, request.Bypassed, request.Scene,
        unchanged = Unchanged, midi_assignment = request.MidiAssignment, transport = command.IsShortMessage ? "configured-cc" : "direct-sysex", persistence = "live-only-no-save", saved_sha256 = PresetBody.Sha256(saved), loaded_sha256 = PresetBody.Sha256(loaded),
        unsaved_changes_present = !SnapshotExport.DecodeFile(Device, saved).Image.RawImage.SequenceEqual(baseline.RawImage),
        scope = "absolute state; no toggle, upload, Save, reload or automatic undo; scene selection may emit configured MIDI and invoke scene-revert behavior",
        hardware_qualified = false, ports_opened = false };
    internal void Claim() { if (Interlocked.Exchange(ref claimed, 1) != 0) throw new InvalidOperationException("Live control scope cannot be reused."); }
    public override void Validate(EditorCommand c)
    { if (!Ready || c.Device != Device || c.Slot != Slot || !c.Frame.SequenceEqual(command.Frame)) throw new InvalidOperationException("Command differs from the exact live control scope."); }
    internal async Task CheckBefore(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        if (Attempted) throw new InvalidOperationException("Live control already attempted; reconcile without replay.");
        var fresh = await CaptureAsync(session, context, timeout, token);
        if (SnapshotExport.Json(fresh) != SnapshotExport.Json(request.Context)) throw new InvalidDataException("Live control context changed; prepare a fresh review.");
    }
    public static async Task<LiveControlContext> CaptureAsync(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var before = await session.CurrentAsync(context, timeout, token);
        var saved = await session.SavedAsync(context, before.Slot, timeout, token); var loaded = await session.EditBufferAsync(context, timeout, token);
        var blocks = await session.BlockStatusAsync(context, timeout, token);
        if (before != await session.CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection drift during live control capture.");
        return new(1, context.Definition.Model, context.Definition.PresetCapacity, context.Firmware, before, blocks.OrderBy(b => b.EffectId).ToArray(), Convert.ToBase64String(saved.Sysex), Convert.ToBase64String(loaded.Sysex));
    }
    private void VerifyAfter(LiveControlContext after)
    {
        if (after.Model != Device.Model || after.Capacity != Device.PresetCapacity || after.Firmware != Firmware || after.Current.Slot != Slot ||
            after.Current.Name != request.Context.Current.Name || after.Current.Scene != (request.Scene ?? request.Context.Current.Scene) ||
            after.SavedCapture != request.Context.SavedCapture) throw new InvalidDataException("Live result differs or saved preset changed.");
        var live = PresetFile.Inspect(Device, Convert.FromBase64String(after.LoadedCapture));
        if (live.Body.Length != baseline.Body.Length || !live.RawImage.AsSpan(0, 4).SequenceEqual(baseline.RawImage.AsSpan(0, 4)) || !live.RawImage.AsSpan(6, 66).SequenceEqual(baseline.RawImage.AsSpan(6, 66)) ||
            Enumerable.Range(0, live.Body.Length).Any(i => !permittedBodyBytes.Contains(i) && live.Body[i] != baseline.Body[i]))
            throw new InvalidDataException("Unrelated loaded settings changed; retained evidence requires reconciliation.");
        if (request.Kind == "scene") return;
        if (stateWord is int offset && !live.Body.AsSpan(offset, 2).SequenceEqual(baseline.Body.AsSpan(offset, 2)))
        {
            int value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(live.Body.AsSpan(offset, 2));
            int expectedValue = request.Kind == "channel" ? request.Channel![0] - 'A' : request.Bypassed!.Value ? 1 : 0;
            if (value != expectedValue) throw new InvalidDataException("Changed scene-state word does not match the requested value.");
        }
        var expected = request.Context.Blocks.Select(b => b.EffectId != request.EffectId ? b : request.Kind == "channel" ? b with { Channel = request.Channel![0] - 'A' + 1 } : b with { Bypassed = request.Bypassed!.Value }).ToArray();
        if (!expected.SequenceEqual(after.Blocks)) throw new InvalidDataException("Live status did not confirm exactly the requested block state.");
    }
    public async Task<object> ExecuteAsync(ReadSession session, DeviceContext context, string destination, TimeSpan timeout, CancellationToken token)
    {
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new live-control evidence directory.");
        Directory.CreateDirectory(destination); string phase = "preflight";
        void Checkpoint(string state, string? error = null)
        {
            string path = Path.Combine(destination, "attempt.json"); byte[] bytes = System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(new { state, phase, error, automatic_retry = false }));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read); stream.Write(bytes); stream.Flush(true);
        }
        File.WriteAllBytes(Path.Combine(destination, "request.json"), JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)); Checkpoint("started");
        try
        {
            session.AuthorizeLiveControl(this); await CheckBefore(session, context, timeout, token); Ready = true;
            if (!Unchanged) { phase = "send-once"; Checkpoint("started"); await session.SendEditorAsync(command, timeout, token); }
            phase = "verify"; Checkpoint("started");
            var after = await CaptureAsync(session, context, timeout, token);
            File.WriteAllText(Path.Combine(destination, "observed.json"), JsonSerializer.Serialize(after, JsonOptions)); VerifyAfter(after);
            phase = "verified-live"; Checkpoint("complete");
            return new { complete = true, unchanged = Unchanged, saved_unchanged = true, phase, automatic_retry = false, hardware_qualified = false };
        }
        catch (Exception e) { Checkpoint("stopped-uncertain", e.Message); throw; }
        finally { Ready = false; }
    }
}
public sealed partial class ReadSession
{
    public void AuthorizeLiveControl(LiveControlScope scope) => AuthorizeEditor(scope);
}
