namespace Fractal.Core;

public sealed record LiveStateSample(DateTimeOffset ObservedAt, CurrentState Current, BlockStatus[] Blocks);
/// <summary>Finite query-based observations. Does not claim complete internal/unsolicited event coverage.</summary>
public static class LiveMonitor
{
    public static async Task<LiveStateSample> SampleAsync(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token)
    {
        var before = await session.CurrentAsync(context, timeout, token);
        var blocks = await session.BlockStatusAsync(context, timeout, token);
        if (before != await session.CurrentAsync(context, timeout, token)) throw new InvalidDataException("Selection drift during live status sample.");
        return new(DateTimeOffset.UtcNow, before, blocks.OrderBy(b => b.EffectId).ToArray());
    }
    public static async Task<object> CaptureAsync(ReadSession session, DeviceContext context, int samples, int intervalMs, string destination, TimeSpan timeout, CancellationToken token)
    {
        if (samples is < 1 or > 120 || intervalMs is < 100 or > 10000) throw new ArgumentException("Choose 1–120 samples with 100–10000 ms between samples.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new private monitor directory.");
        Directory.CreateDirectory(destination); var rows = new List<LiveStateSample>(); var changed = new List<int>();
        string? failure = null;
        object Report(bool complete) => new { model = context.Definition.Model, capacity = context.Definition.PresetCapacity, firmware = context.Firmware,
            samples_requested = samples, interval_ms = intervalMs, complete, samples = rows, changed_sample_indices = changed, failure,
            coverage = "sampled selection and effective block status only; transient changes, other parameters and unsolicited notifications are not completely observed",
            hardware_qualified = false };
        void Save(bool complete)
        {
            string path = Path.Combine(destination, "monitor.json"), temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(Report(complete)))); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        Save(false);
        try
        {
            for (int index = 0; index < samples; index++)
            {
                if (index > 0) await Task.Delay(intervalMs, token);
                var sample = await SampleAsync(session, context, timeout, token);
                if (rows.Count > 0 && (sample.Current != rows[^1].Current || !sample.Blocks.SequenceEqual(rows[^1].Blocks))) changed.Add(index);
                rows.Add(sample); Save(false);
            }
            Save(true); return Report(true);
        }
        catch (Exception e) { failure = e.Message; Save(false); throw; }
    }
}
