namespace Fractal.Core;

public sealed partial class ReadSession
{
    public Task<BlockParameterCapture> BlockParametersAsync(DeviceContext context, int effectId, TimeSpan timeout,
        CancellationToken token, Action<byte[]>? retain = null)
    {
        var decoder = new BlockParameterDecoder(context.Definition, effectId);
        return Exchange(SysexProtocol.BlockParametersQuery(context.Definition, effectId), frame =>
        {
            if (frame.Length >= 6 && frame[4] == context.Definition.ModelByte && frame[5] is 0x74 or 0x75 or 0x76) retain?.Invoke(frame);
            var result = decoder.Accept(frame);
            return (result != null, result!);
        }, timeout, token);
    }
}

public static class BlockParameterRead
{
    public static async Task<object> CaptureAsync(ReadSession session, DeviceContext context, int effectId, string destination,
        TimeSpan timeout, CancellationToken token)
    {
        _ = SysexProtocol.BlockParametersQuery(context.Definition, effectId);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new private block parameter directory.");
        Directory.CreateDirectory(destination);
        using var wire = new FileStream(Path.Combine(destination, "received.syx"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        LiveStateSample? before = null, after = null; BlockParameterCapture? table = null; string? failure = null;
        object Report(bool complete) => new { schema_version = 1, model = context.Definition.Model, capacity = context.Definition.PresetCapacity,
            firmware = context.Firmware, effect_id = effectId, complete, before, after, table, failure, raw_file = "received.syx",
            hardware_qualified = false, saved_state_verified = false,
            coverage = "bounded private current-block table; sampled selection/status stability cannot exclude transient drift or guarantee an atomic snapshot" };
        void Save(bool complete)
        {
            string path = Path.Combine(destination, "parameters.json");
            using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { file.Write(System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(Report(complete)))); file.Flush(true); }
            File.Move(path + ".tmp", path, true);
        }
        Save(false);
        try
        {
            before = await LiveMonitor.SampleAsync(session, context, timeout, token); Save(false);
            if (!before.Blocks.Any(b => b.EffectId == effectId)) throw new InvalidDataException("Requested block instance is not in current block status.");
            table = await session.BlockParametersAsync(context, effectId, timeout, token, frame =>
            {
                if (wire.Length + frame.Length > 262144) throw new IOException("Private block capture exceeds bound.");
                wire.Write(frame); wire.Flush(true);
            }); Save(false);
            after = await LiveMonitor.SampleAsync(session, context, timeout, token);
            if (before.Current != after.Current || !before.Blocks.SequenceEqual(after.Blocks)) throw new InvalidDataException("Selection/block state drift during parameter capture.");
            Save(true); return Report(true);
        }
        catch (Exception error) { failure = error.Message; Save(false); throw; }
    }
}
