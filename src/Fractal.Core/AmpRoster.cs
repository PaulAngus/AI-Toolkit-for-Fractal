using System.Text;
namespace Fractal.Core;

public sealed record AmpRosterEntry(int Id, string Name, string ReplySha256);
/// <summary>Bounded ordinal queries; replies carry no ordinal, so failures end the session.</summary>
public static class AmpRoster
{
    public static int TypeParameter(DeviceDefinition d) => AudioParameterMappings.ForModel(d.Model, "Amp").Entries.Single(e => e.Symbol == "DISTORT_TYPE").ParameterId;
    public static byte[] Query(DeviceDefinition d, int ordinal)
    {
        if (ordinal is < 0 or > 16383) throw new ArgumentOutOfRangeException(nameof(ordinal));
        byte[] p = new byte[15]; p[0] = 31; p[2] = 58; p[4] = (byte)TypeParameter(d);
        p[6] = (byte)(ordinal & 127); p[7] = (byte)(ordinal >> 7);
        return SysexProtocol.Frame(d.ModelByte, 1, p);
    }
    internal static bool IsQuery(byte[] f)
    {
        if (f.Length != 23 || f[6] != 31 || f[7] != 0 || f[8] != 58 || f[9] != 0 || f[11] != 0 || f.Skip(14).Take(7).Any(b => b != 0)) return false;
        var d = DeviceDefinition.Create(DeviceDefinition.ModelName(f[4]), f[4] == 16 ? 1024 : 512);
        return f[10] == TypeParameter(d);
    }
    public static AmpRosterEntry Decode(DeviceDefinition d, int ordinal, byte[] f)
    {
        if (ordinal is < 0 or > 16383) throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (!SysexProtocol.ValidFrame(f, d.ModelByte, 1) || f.Length < 25 || f.Length > 320 ||
            f[6] != 31 || f[7] != 0 || f[8] != 58 || f[9] != 0 || (f[10] | f[11] << 7) != TypeParameter(d))
            throw new InvalidDataException("Invalid amp roster reply descriptor/address/checksum.");
        int length = f[19] | f[20] << 7;
        if (length is < 2 or > 64 || f.Length != 23 + length + (length + 6) / 7)
            throw new InvalidDataException("Invalid roster display-string length.");
        // Seven raw bytes occupy eight MSB-first septets; partial groups restart the packing.
        var raw = new byte[length]; int wire = 21;
        for (int start = 0; start < length; start += 7)
        {
            int n = Math.Min(7, length - start);
            for (int i = 0; i < n; i++) raw[start + i] = (byte)(((f[wire + i] << (i + 1)) | (f[wire + i + 1] >> (6 - i))) & 255);
            if ((f[wire + n] & ((1 << (7 - n)) - 1)) != 0) throw new InvalidDataException("Nonzero roster string padding.");
            wire += n + 1;
        }
        int nul = Array.IndexOf(raw, (byte)0);
        if (nul < 1 || raw.Take(nul).Any(b => b is < 32 or > 126) || raw.Skip(nul).Any(b => b != 0)) throw new InvalidDataException("Unknown roster name encoding or empty entry; not an end marker.");
        string name = Encoding.ASCII.GetString(raw, 0, nul);
        return new(ordinal, name, PresetBody.Sha256(f));
    }
    public static async Task<object> CaptureAsync(ReadSession session, DeviceContext context, int start, int count, string destination, TimeSpan timeout, CancellationToken token)
    {
        if (count is < 1 or > 1024 || start is < 0 or > 16383 || (long)start + count > 16384) throw new ArgumentException("Choose an explicit ordinal range, count 1–1024, ending at most at 16384.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new private roster directory.");
        Directory.CreateDirectory(destination); var entries = new List<AmpRosterEntry>(); CurrentState? before = null, after = null; string? failure = null;
        object Report(bool complete) => new { schema_version = 1, protocol_release = ProtocolRelease.Version, model = context.Definition.Model,
            firmware = context.Firmware, start, count, entries, range_complete = complete, roster_complete = false,
            coverage = "requested-ordinals-only; no firmware-wide termination inferred", before, after, failure,
            correlation = "serialized; reply omits ordinal; late duplicates cannot be uniquely identified", hardware_qualified = false };
        void Save(bool complete)
        {
            string path = Path.Combine(destination, "roster.json"), temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(Encoding.UTF8.GetBytes(SnapshotExport.Json(Report(complete)))); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        Save(false);
        try
        {
            before = await session.CurrentAsync(context, timeout, token);
            for (int id = start; id < start + count; id++)
            {
                var (entry, frame) = await session.AmpRosterEntryAsync(context, id, timeout, token);
                File.WriteAllBytes(Path.Combine(destination, $"entry-{id:D5}.syx"), frame); entries.Add(entry); Save(false);
                await Task.Delay(10, token);
            }
            after = await session.CurrentAsync(context, timeout, token);
            if (before != after) throw new InvalidDataException("Selection changed during roster capture.");
            Save(true); return Report(true);
        }
        catch (Exception e) { failure = e.Message; Save(false); throw; }
    }
}
public sealed partial class ReadSession
{
    public Task<(AmpRosterEntry Entry, byte[] Frame)> AmpRosterEntryAsync(DeviceContext context, int ordinal, TimeSpan timeout, CancellationToken token) =>
        Exchange(AmpRoster.Query(context.Definition, ordinal), frame =>
        {
            if (frame.Length < 12 || frame[4] != context.Definition.ModelByte || frame[5] != 1 || frame[6] != 31 || frame[7] != 0 ||
                frame[8] != 58 || frame[9] != 0 || (frame[10] | frame[11] << 7) != AmpRoster.TypeParameter(context.Definition)) return (false, default);
            return (true, (AmpRoster.Decode(context.Definition, ordinal, frame), frame));
        }, timeout, token);
}
