using System.Buffers.Binary;
using Fractal.Core;

// Independent literal expectations and synthetic bodies, never assembled from the production catalogues.
static class AudioRoutingChecks
{
    public static IEnumerable<(string Name, Action Test)> Cases(DeviceDefinition[] definitions, string? destination)
    {
        foreach (var d in definitions)
        {
            yield return ($"{d.Model}/{d.PresetCapacity} editor addresses remain separate model-specific evidence", () =>
            {
                var all = EditReadCatalogue.Parameters(d, "99.07");
                var editor = all.EditorParameters;
                int count = d.Model switch { "FM9" => 1577, "FM3" => 1581, _ => 1651 };
                int drive = d.Model switch { "FM9" => 11, "FM3" => 7, _ => 1 };
                Need(editor.Entries.Length == count && editor.Model == d.Model, "Complete own-model audio catalogue");
                Need(editor.Entries.Single(e => e.Symbol == "DISTORT_DRIVE").ParameterId == drive, "No cross-model ID reuse");
                Need(editor.Entries.Any(e => !e.Addressable) && editor.Entries.Any(e => e.UnitHint == "unverified"), "Unknown and nonaddressable retained");
                Need(editor.CatalogueSha256.Length == 64 && editor.SourceSha256.Length == 64 && !editor.WriteSupported && !editor.HardwareQualified, "Evidence and no write claim");
                Need(editor.AddressSpace == "editor-parameter-id-not-saved-offset", "Explicit address domain");
                var reverb = EditReadCatalogue.Parameters(d, "99.07", "rEvErB");
                Need(reverb.Parameters.Single().Key == "reverb.type", "Case-insensitive saved family");
                Need(reverb.EditorParameters.Entries.Single(e => e.Symbol == "REVERB_TYPE").ParameterId == (d.Model == "FM9" ? 10 : 0), "Editor reverb ID");
                var unknown = EditReadCatalogue.Parameters(d, "99.07", "Unknown");
                Need(unknown.Parameters.Length == 0 && unknown.EditorParameters.Entries.Length == 0 && unknown.Limitations.Length > 0, "Unknown family not borrowed");
                if (d.Model == "FM3") Need(editor.Entries.Where(e => e.EditorFamily == "FLANGER" && e.ParameterId == 1).Count() == 2, "Legacy ID aliases preserved");
                Need(all.Parameters.All(p => p.SavedEncoding == "uint16-little-endian" && p.WriteSupported), "Mapped saved descriptors support authored editing");
            });
            yield return ($"{d.Model}/{d.PresetCapacity} saved audio fields and channel scaling preserve raw words", () =>
            {
                var f = Build(d);
                var report = Decode(d, f.Body);
                var amp = report.Blocks.Single(b => b.EffectId == 58);
                if (d.Model == "Axe-Fx III") Need(amp.Parameters.All(p => p.Key == "amp.type"), "No imported FM9 knob offsets for III");
                else
                {
                    Need(amp.Parameters.Length == 40, "Type plus nine knobs on four channels");
                    var drive = amp.Parameters.Where(p => p.Key == "amp.drive").ToArray();
                    Need(drive.Select(p => p.RawValue).SequenceEqual(new[] { 0, 32768, 65535, 16384 }), "Raw values exact");
                    Need(drive[0].Value == 0 && drive[2].Value == 10 && Math.Abs(drive[1].Value!.Value - 32768 * 10.0 / 65535) < 1e-12, "0-10 endpoints and midpoint");
                    Need(amp.Parameters.Single(p => p.Key == "amp.negative_feedback" && p.Channel == "D").RawValue == 608, "Farthest knob on last channel");
                }
                foreach (var (id, key) in new[] { (66, "reverb.type"), (70, "delay.type"), (118, "drive.type"), (78, "chorus.type") })
                {
                    var values = report.Blocks.Single(b => b.EffectId == id).Parameters;
                    Need(values.Length == 4 && values.All(p => p.Key == key), "Exact effect profile");
                    Need(values.Select(p => p.RawValue).SequenceEqual(new[] { 60000, 60001, 60002, 60003 }), "Unresolved IDs retained across channels");
                }
                Need(Convert.FromHexString(report.BodyHex).SequenceEqual(f.Body), "Full body preservation");
                if (destination != null)
                {
                    string folder = Path.Combine(destination, $"Audio-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, "synthetic.syx"), Fixtures.FromBody(d, 0, f.Body).Sysex);
                }
            });
            yield return ($"{d.Model}/{d.PresetCapacity} Cab discriminator prevents interpreting other modes", () =>
            {
                foreach (int cols in d.Model == "FM3" ? new[] { 101, 106 } : new[] { d.Model == "FM9" ? 101 : 105 })
                {
                    var f = Build(d, cols);
                    var cab = Decode(d, f.Body).Blocks.Single(b => b.EffectId == 62);
                    Need(cab.Parameters.Length == 16, "All raw Cab fields");
                    Need(cab.Parameters.Single(p => p.Key == "cab.mode" && p.Channel == "D").RawValue == 65535, "Unknown mode preserved");
                    foreach (var p in cab.Parameters.Where(p => p.Key != "cab.mode"))
                    {
                        Need(p.RawValue >= 50000, "Raw conditional fields retained");
                        Need(p.Channel is "A" or "C" ? p.Value == p.RawValue && p.Status == "decoded-candidate" : p.Value == null && p.Status == "not-applicable", "Mode-specific applicability");
                    }
                }
            });
            yield return ($"{d.Model}/{d.PresetCapacity} routing decodes fan-out fan-in cross-row and shunt edges", () =>
            {
                var f = Build(d);
                var report = Decode(d, f.Body);
                var route = report.Routing;
                var expected = new[] { "cell:1:1>cell:2:2", $"cell:{d.GridRows}:1>cell:2:2", "cell:1:1>cell:3:2", "cell:2:2>cell:1:3", "cell:3:2>cell:1:3" };
                Need(route.Status == "decoded-candidate" && route.Edges!.Select(e => e.Source + ">" + e.Destination).SequenceEqual(expected), "Incoming destination mask, previous-column sources");
                Need(route.Nodes.Single(n => n.Key == "cell:2:2").Kind == "shunt", "Shunt remains a graph node");
                Need(!route.HardwareQualified && !report.ExecutionSupported && !report.PortsOpened, "Offline candidate only");
                Need(SnapshotExport.Json(report) == SnapshotExport.Json(Decode(d, f.Body)), "Repeat deterministic");
            });
            yield return ($"{d.Model}/{d.PresetCapacity} routing unknown bits and invalid endpoints never fabricate edges", () =>
            {
                var f = Build(d);
                int bits = d.Model == "FM3" ? 0x8010 : 0x8040;
                Put(f.Body, Cell(d, 2, 2) + 2, bits | 1 | (1 << (d.GridRows - 1)));
                Put(f.Body, Cell(d, 1, 1) + 2, 1); // no prior grid column
                Put(f.Body, Cell(d, 4, 2) + 2, 1); // empty destination
                Put(f.Body, Cell(d, 1, 3) + 2, 7); // row 1 source empty, rows 2/3 valid
                var report = Decode(d, f.Body);
                Need(report.Routing.Edges!.Length == 5 && report.Routing.Status == "partial", "Preserve valid subset");
                Need(report.Routing.Words.Single(w => w.Row == 2 && w.Column == 2).UnknownBits == bits, "No truncation or borrowed six-row mask");
                foreach (string code in new[] { "unknown-routing-bits", "unresolved-routing-boundary", "routing-empty-destination", "routing-empty-source" })
                    Need(report.Issues.Any(i => i.Code == code), "Actionable route issue " + code);
                Need(Convert.FromHexString(report.Routing.RawGridHex).SequenceEqual(f.Body.AsSpan(0x104, d.GridRows * d.GridColumns * 4).ToArray()), "Lossless grid");
                Array.Clear(f.Body, 0x104, d.GridRows * d.GridColumns * 4);
                var empty = Decode(d, f.Body).Routing;
                Need(empty.Edges is { Length: 0 } && empty.Nodes.Length == 0 && empty.Status == "decoded-candidate", "Decoded empty grid differs from unknown topology");
            });
        }
    }
    private static PresetEditReadReport Decode(DeviceDefinition d, byte[] body) => PresetEditProjection.Decode(d, "99.07", Fixtures.FromBody(d, 0, body).Sysex);
    private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)value);
    private static int Cell(DeviceDefinition d, int row, int col) => 0x104 + ((col - 1) * d.GridRows + row - 1) * 4;
    internal static (byte[] Body, Dictionary<int, int> Offsets) Build(DeviceDefinition d, int? cabColumns = null)
    {
        byte[] body = new byte[9000];
        int pos = 0x400;
        var offsets = new Dictionary<int, int>();
        void Record(int id, int cols, int rows)
        { offsets[id] = pos; Put(body, pos - 12, id); Put(body, pos + 30, cols); Put(body, pos + 32, rows); pos += (23 + cols * rows) * 2; }
        Record(2, 25, 1); Record(35, 25, 1);
        int ampCols = d.Model switch { "FM9" => 147, "FM3" => 144, _ => 142 };
        int ampFirst = d.Model switch { "FM9" => 54, "FM3" => 46, _ => 34 };
        Record(58, ampCols, 4);
        for (int ch = 0; ch < 4; ch++)
        {
            int basis = offsets[58] + ampFirst + ch * ampCols * 2;
            Put(body, basis, 60000 + ch);
            if (d.Model != "Axe-Fx III")
            {
                int[] deltas = [2, 4, 6, 8, 10, 32, 38, 40, 42];
                for (int k = 0; k < deltas.Length; k++) Put(body, basis + deltas[k], k == 0 ? new[] { 0, 32768, 65535, 16384 }[ch] : 600 + k);
            }
        }
        int cc = cabColumns ?? (d.Model == "Axe-Fx III" ? 105 : 101);
        Record(62, cc, 4);
        for (int ch = 0; ch < 4; ch++)
        {
            int basis = offsets[62] + 46 + ch * cc * 2;
            Put(body, basis + 50, new[] { 1, 0, 1, 65535 }[ch]);
            Put(body, basis + 158, 50000 + ch); Put(body, basis + 160, 50100 + ch); Put(body, basis + 166, 50200 + ch);
        }
        foreach (var (id, cols, first) in new[] { (66, d.Model == "FM9" ? 73 : 71, d.Model == "FM9" ? 54 : 34),
            (70, d.Model == "FM9" ? 90 : 88, d.Model == "FM9" ? 54 : d.Model == "FM3" ? 46 : 34), (118, 42, 34), (78, 29, 34) })
        {
            Record(id, cols, 4);
            for (int ch = 0; ch < 4; ch++) Put(body, offsets[id] + first + ch * cols * 2, 60000 + ch);
        }
        Put(body, Cell(d, 1, 1), 58); Put(body, Cell(d, d.GridRows, 1), 62);
        Put(body, Cell(d, 2, 2), 1025); Put(body, Cell(d, 2, 2) + 2, 1 | (1 << (d.GridRows - 1)));
        Put(body, Cell(d, 3, 2), 70); Put(body, Cell(d, 3, 2) + 2, 1);
        Put(body, Cell(d, 1, 3), 66); Put(body, Cell(d, 1, 3) + 2, 6);
        Put(body, Cell(d, 1, 4), 118); Put(body, Cell(d, 1, 5), 78);
        body[^1] = 0xa5;
        return (body, offsets);
    }
}
