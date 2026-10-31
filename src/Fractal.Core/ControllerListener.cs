using System.Text.Json;
using System.Threading.Channels;
namespace Fractal.Core;

public sealed record MidiShortPacket(uint Message, uint DriverMilliseconds);
public interface IMidiInputSource
{
    string TimestampSource => "transport-defined";
    event Action<MidiShortPacket>? ShortReceived;
    event Action<Exception>? Faulted;
}
public sealed record ControllerListenRequest(int SchemaVersion, string Model, int Capacity, string Firmware, MidiRouteRule[] Rules);
public sealed record ControllerObservation(int Index, uint DriverMilliseconds, MidiInputEvent Input, MidiRouteTarget? Target);

/// <summary>Receive-only, bounded listener. Targets remain intents; this API has no send operation.</summary>
public static class ControllerListener
{
    public static ControllerListenRequest Parse(byte[] json)
    {
        if (json.Length > 65536) throw new InvalidDataException("Listener request exceeds bound.");
        var request = JsonSerializer.Deserialize<ControllerListenRequest>(json, LiveControlScope.JsonOptions) ?? throw new InvalidDataException("Missing listener request.");
        if (request.SchemaVersion != 1) throw new InvalidDataException("Unknown listener schema.");
        _ = new MidiInputRouter(DeviceDefinition.Create(request.Model, request.Capacity), request.Firmware, request.Rules);
        return request;
    }
    public static MidiInputEvent? Decode(uint packed)
    {
        int status = (int)(packed & 255), family = status & 240;
        if (status < 128) throw new InvalidDataException("Short MIDI message lacks expanded status.");
        if (family is not (128 or 144 or 176)) return null;
        int number = (int)(packed >> 8 & 255), value = (int)(packed >> 16 & 255);
        if (number > 127 || value > 127) throw new InvalidDataException("Invalid short MIDI data byte.");
        return new(family == 128 ? "note-off" : family == 144 ? "note-on" : "cc", (status & 15) + 1, number, value);
    }
    public static async Task<object> CaptureAsync(IMidiInputSource source, ControllerListenRequest input, string destination,
        TimeSpan duration, int maximumEvents, CancellationToken token)
    {
        var request = Parse(JsonSerializer.SerializeToUtf8Bytes(input, LiveControlScope.JsonOptions));
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(1) || maximumEvents is < 1 or > 4096) throw new ArgumentException("Listener duration must be positive up to one hour; event limit 1–4096.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new private listener directory.");
        Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "request.json"), SnapshotExport.Json(request));
        var router = new MidiInputRouter(DeviceDefinition.Create(request.Model, request.Capacity), request.Firmware, request.Rules);
        var channel = Channel.CreateBounded<MidiShortPacket>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait });
        Exception? failure = null; var events = new List<ControllerObservation>(); string reason = "listening";
        var subscriptionGate = new object(); bool accepting = true;
        void Fail(Exception error) { lock (subscriptionGate) { if (!accepting) return; Interlocked.CompareExchange(ref failure, error, null); channel.Writer.TryComplete(error); } }
        void Receive(MidiShortPacket packet) { lock (subscriptionGate) { if (accepting && !channel.Writer.TryWrite(packet)) Fail(new IOException("Controller input queue overflow; event coverage is incomplete.")); } }
        void Stop() { lock (subscriptionGate) accepting = false; source.ShortReceived -= Receive; source.Faulted -= Fail; }
        object Report(bool complete) => new { schema_version = 1, request.Model, request.Capacity, request.Firmware, complete, reason,
            context_origin = "caller-supplied-routing-context; input-only listener does not identify Fractal hardware",
            timestamp_source = source.TimestampSource,
            failure = failure?.Message, events, duration_limit_seconds = duration.TotalSeconds, event_limit = maximumEvents,
            sends = 0, automatic_execution = false, hardware_qualified = false, coverage = "received note/CC events during this bounded subscription; other messages ignored" };
        void Save(bool complete)
        {
            string path = Path.Combine(destination, "listener.json");
            using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { file.Write(System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(Report(complete)))); file.Flush(true); }
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(duration);
        Save(false); source.ShortReceived += Receive; source.Faulted += Fail;
        try
        {
            while (events.Count < maximumEvents)
            {
                MidiShortPacket packet;
                try { packet = await channel.Reader.ReadAsync(deadline.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { reason = "duration-limit"; break; }
                if (Volatile.Read(ref failure) is Exception error) throw new IOException("Controller listener stopped with incomplete input.", error);
                var decoded = Decode(packet.Message); if (decoded == null) continue;
                events.Add(new(events.Count, packet.DriverMilliseconds, decoded, router.Route(decoded))); Save(false);
            }
            token.ThrowIfCancellationRequested();
            Stop();
            if (Volatile.Read(ref failure) is Exception finalError) throw new IOException("Controller listener stopped with incomplete input.", finalError);
            if (events.Count == maximumEvents) reason = "event-limit";
            Save(true); return Report(true);
        }
        catch (Exception error) { failure ??= error; reason = token.IsCancellationRequested ? "cancelled" : "failed"; Save(false); throw; }
        finally { Stop(); channel.Writer.TryComplete(); }
    }
}
