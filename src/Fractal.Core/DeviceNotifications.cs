using System.Threading.Channels;
namespace Fractal.Core;

public sealed record DeviceNotification(string Kind, string Origin, bool Valid, object? Value, string[] Refresh, string? Error, string RawHex);
public sealed record NotificationObservation(int Index, DateTimeOffset ReceivedAt, DeviceNotification Notification);

/// <summary>Observed wire facts only: no unsolicited-origin inference for reply formats and no saved-state claims.</summary>
public static class DeviceNotifications
{
    internal static bool IsPublishedPush(byte[] frame) =>
        frame.Length is 7 or 10 && frame[0] == 240 && frame[1] == 0 && frame[2] == 1 && frame[3] == 0x74 &&
        frame[4] is 16 or 17 or 18 && frame[^1] == 247 &&
        (frame.Length == 7 && frame[5] == 16 || frame.Length == 10 && frame[5] == 17 && frame[6] < 12 && frame[7] < 6 && frame[8] < 128);
    public static DeviceNotification Decode(DeviceDefinition device, byte[] frame)
    {
        string raw = Convert.ToHexString(frame);
        DeviceNotification Result(string kind, object? value, params string[] refresh) => new(kind,
            kind is "tempo-beat" or "tuner-pitch" ? "manufacturer-push-format" : "response-format; solicitation-unknown", true, value, refresh, null, raw);
        try
        {
            if (frame.Length < 7 || frame[0] != 240 || frame[1] != 0 || frame[2] != 1 || frame[3] != 0x74 || frame[^1] != 247)
                return new("other-midi", "unknown", false, null, [], "Not a Fractal envelope.", raw);
            if (frame[4] != device.ModelByte) return new("other-model", "unknown", false, null, [], "Different model address.", raw);
            if (frame.Skip(1).Take(frame.Length - 2).Any(b => b > 127)) throw new InvalidDataException("Non-septet payload.");
            // Published push packets intentionally omit the checksum. Never weaken normal reply validation.
            if (frame[5] == 0x10 && frame.Length == 7) return Result("tempo-beat", new { });
            if (frame[5] == 0x11 && frame.Length == 10)
            {
                if (frame[6] > 11 || frame[7] > 5) throw new InvalidDataException("Invalid tuner note/string.");
                return Result("tuner-pitch", new { note = frame[6], @string = frame[7], cents = frame[8] - 63 });
            }
            if (!SysexProtocol.ValidFrame(frame, device.ModelByte, frame[5])) throw new InvalidDataException("Invalid checksum/framing.");
            int count = frame.Length - 8;
            switch (frame[5])
            {
                case 1:
                    var echo = PrivateParameters.DecodeEcho(device, frame);
                    return echo == null ? Result("unknown-fractal", new { function = frame[5] }, "live-state-coverage-unknown") :
                        Result("private-parameter-echo", echo, "edit-buffer");
                case 0x64:
                    if (count is < 2 or > 4) throw new InvalidDataException("Invalid private result length.");
                    return Result("private-result", new { echoed_function = frame[6], result_code = frame[7], extra = frame.Skip(8).Take(count - 2).ToArray(),
                        interpretation = "raw result only; neither universal success nor persistence inferred" });
                case 0x74:
                    if (frame.Length is not (12 or 13) || (frame[6] | frame[7] << 7) is < 1 or > 1000 || (frame[8] | frame[9] << 7) is < 1 or > 4096)
                        throw new InvalidDataException("Invalid private block header.");
                    return Result("private-block-header", new { effect_id = frame[6] | frame[7] << 7, count = frame[8] | frame[9] << 7, metadata = frame.Length == 13 ? (int?)frame[10] : null }, "edit-buffer");
                case 0x75:
                    int pageFields = frame[6] | frame[7] << 7;
                    if (frame.Length is < 13 or > 778 || (frame.Length - 10) % 3 != 0 ||
                        pageFields is < 1 or > 256 || pageFields != (frame.Length - 10) / 3 ||
                        Enumerable.Range(0, (frame.Length - 10) / 3).Any(i => frame[10 + 3 * i] > 3)) throw new InvalidDataException("Invalid private block section.");
                    return Result("private-block-section", new { field_count = pageFields, count_encoding = "little-first-14-bit" }, "edit-buffer");
                case 0x76:
                    if (frame.Length != 8) throw new InvalidDataException("Invalid private block end.");
                    return Result("private-block-end", new { }, "edit-buffer");
                case 0x0a:
                case 0x0b:
                    if (count != 3 || (frame[6] | frame[7] << 7) is < 1 or > 1000 || frame[8] > (frame[5] == 10 ? 1 : 3))
                        throw new InvalidDataException("Invalid block state address/value.");
                    int id = frame[6] | frame[7] << 7;
                    return frame[5] == 10 ? Result("block-bypass", new { effect_id = id, bypassed = frame[8] == 1 }, "live-block-status") :
                        Result("block-channel", new { effect_id = id, channel = frame[8] + 1 }, "live-block-status", "edit-buffer");
                case 0x0c:
                    if (count != 1 || frame[6] > 7) throw new InvalidDataException("Invalid scene.");
                    return Result("scene", new { scene = frame[6] + 1 }, "current-selection", "live-block-status", "edit-buffer");
                case 0x0d:
                    if (count != 34) throw new InvalidDataException("Invalid preset-name length.");
                    int slot = SysexProtocol.PresetAddress(frame); device.ValidateSlot(slot);
                    return Result("preset-name", new { slot, name = SysexProtocol.ReadName(frame.AsSpan(8, 32)) }, "current-selection", "preset-names");
                case 0x0e:
                    if (count != 33 || frame[6] > 7) throw new InvalidDataException("Invalid scene-name length/address.");
                    return Result("scene-name", new { scene = frame[6] + 1, name = SysexProtocol.ReadName(frame.AsSpan(7, 32)), preset_slot = (int?)null }, "scene-names");
                case 0x0f:
                    if (count != 1 || frame[6] > 63) throw new InvalidDataException("Invalid looper status.");
                    return Result("looper", new { record = (frame[6] & 1) != 0, play = (frame[6] & 2) != 0, overdub = (frame[6] & 4) != 0,
                        once = (frame[6] & 8) != 0, reverse = (frame[6] & 16) != 0, half_speed = (frame[6] & 32) != 0 });
                case 0x11:
                    if (count != 1 || frame[6] > 1) throw new InvalidDataException("Invalid tuner state.");
                    return Result("tuner-state", new { enabled = frame[6] == 1 });
                case 0x13: return Result("block-status", BlockStatusTable.Decode(device, frame), "live-block-status");
                case 0x14:
                    if (count != 2 || (frame[6] | frame[7] << 7) == 16383) throw new InvalidDataException("Invalid tempo response.");
                    return Result("tempo", new { bpm = frame[6] | frame[7] << 7 });
                default: return Result("unknown-fractal", new { function = frame[5] }, "live-state-coverage-unknown");
            }
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { return new("malformed-fractal", "unknown", false, null, ["live-state-coverage-unknown"], error.Message, raw); }
    }
    public static async Task<object> CaptureAsync(IMidiTransport source, DeviceDefinition device, string firmware, string destination,
        TimeSpan duration, int maximumEvents, CancellationToken token)
    {
        _ = EditReadCatalogue.Capabilities(device, firmware);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(1) || maximumEvents is < 1 or > 4096) throw new ArgumentException("Duration must be positive up to one hour; event bound 1–4096.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new private notification directory.");
        Directory.CreateDirectory(destination);
        using var wire = new FileStream(Path.Combine(destination, "received.syx"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var queue = Channel.CreateBounded<(DateTimeOffset At, byte[] Bytes)>(256);
        var observations = new List<NotificationObservation>(); var assembler = new SysexAssembler(); var observer = new DeviceNotificationObserver(device);
        var gate = new object(); bool accepting = true; Exception? failure = null; string reason = "listening";
        void Fail(Exception error) { lock (gate) { if (!accepting) return; failure ??= error; queue.Writer.TryComplete(error); } }
        void Receive(byte[] bytes)
        {
            lock (gate)
            {
                if (!accepting) return;
                if (bytes.Length > 65536 || !queue.Writer.TryWrite((DateTimeOffset.UtcNow, bytes.ToArray()))) Fail(new IOException("Notification queue/input bound exceeded; coverage is incomplete."));
            }
        }
        void Stop() { lock (gate) accepting = false; source.Received -= Receive; source.Faulted -= Fail; }
        object Report(bool complete) => new { schema_version = 1, model = device.Model, capacity = device.PresetCapacity, firmware, complete, reason,
            observations, failure = failure?.Message, midi_sends = 0, hardware_qualified = false, saved_state_verified = false,
            raw_file = "received.syx", raw_bytes = wire.Length,
            context_origin = "caller-supplied; passive capture does not identify hardware",
            source_scope = device.Model == "Axe-Fx III" ? "manufacturer third-party MIDI specification; exact firmware/output coverage unqualified" : "shared Gen3 envelope interpretation; notification emission on this model/firmware unqualified",
            coverage = "received frames during bounded subscription only; complete collection does not establish complete device-change coverage",
            private_source_scope = "gen3-editor-actions-pinned open implementation; own-model parameter catalogues; no new hardware qualification",
            invalid_count = observations.Count(o => !o.Notification.Valid),
            malformed_count = observations.Count(o => o.Notification.Kind == "malformed-fractal"), unknown_count = observations.Count(o => o.Notification.Kind == "unknown-fractal"),
            refresh_required = observations.SelectMany(o => o.Notification.Refresh).Distinct().Order().ToArray() };
        void Save(bool complete)
        {
            string path = Path.Combine(destination, "notifications.json");
            using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { file.Write(System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(Report(complete)))); file.Flush(true); }
            File.Move(path + ".tmp", path, true);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(duration);
        Save(false); source.Received += Receive; source.Faulted += Fail;
        try
        {
            while (observations.Count < maximumEvents)
            {
                (DateTimeOffset At, byte[] Bytes) item;
                try { item = await queue.Reader.ReadAsync(deadline.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { reason = "duration-limit"; break; }
                if (failure != null) throw new IOException("Notification source failed.", failure);
                if (wire.Length + item.Bytes.Length > 4_000_000) throw new IOException("Notification raw capture exceeds four MB bound.");
                wire.Write(item.Bytes); wire.Flush(true);
                foreach (var frame in assembler.Feed(item.Bytes))
                {
                    observations.Add(new(observations.Count, item.At, observer.Observe(frame))); Save(false);
                    if (observations.Count == maximumEvents) break;
                }
            }
            token.ThrowIfCancellationRequested(); Stop();
            if (failure != null) throw new IOException("Notification source failed.", failure);
            if (assembler.Incomplete) throw new IOException("Capture ended during an incomplete SysEx frame.");
            if (observer.Incomplete) throw new IOException("Capture ended during an incomplete private block table.");
            if (observations.Count == maximumEvents) reason = "event-limit";
            Save(true); return Report(true);
        }
        catch (Exception error) { failure ??= error; reason = token.IsCancellationRequested ? "cancelled" : "failed"; Save(false); throw; }
        finally { Stop(); queue.Writer.TryComplete(); }
    }
}
