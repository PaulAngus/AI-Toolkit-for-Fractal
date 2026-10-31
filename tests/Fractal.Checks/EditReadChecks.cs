using System.Buffers.Binary;
using System.Text;
using Fractal.Core;

// Synthetic only, independently assembled fixed layout expectations. No profile/production
// offset resolver participates in fixture assembly. The existing fixture packer only adds integrity framing.
static class EditReadChecks
{
    public static IEnumerable<(string Name, Action Test)> Cases(DeviceDefinition[] definitions, string? destination)
    {
        foreach (var d in definitions)
        {
            yield return ($"{d.Model}/{d.PresetCapacity} edit catalogue is offline and model-specific", () =>
            {
                var caps = EditReadCatalogue.Capabilities(d, "99.07");
                Need(!caps.PortsOpened && caps.ExecutionSupported && !caps.Scope.HardwareQualified, "Execution support separated from physical qualification");
                Need(caps.Scope.Model == d.Model && caps.Scope.Capacity == d.PresetCapacity && !caps.Scope.HistoricalLayoutEvidence, "Context qualification");
                Need(caps.Capabilities.Single(c => c.Operation == "connected-insertion-removal").WriteSupported, "Connected execution supported");
                Need(caps.Capabilities.Single(c => c.Operation == "live-block-control").WriteSupported, "Supported live tools remain enabled across model definitions");
                Need(caps.Capabilities.Single(c => c.Operation == "routing-graph").ReadSupport == "candidate-offline-read", "Candidate graph only");
                var parameter = EditReadCatalogue.Parameters(d, "99.07", "Amp").Parameters.Single(p => p.Key == "amp.type");
                int firstOffset = d.Model switch { "FM9" => 54, "FM3" => 46, _ => 34 };
                Need(parameter.SavedOffsets[0] == firstOffset && parameter.Key == "amp.type", "Independent model fields");
                Need(EditReadCatalogue.Parameters(d, "99.07", "Cab").Parameters.Length >= 4, "Cab saved fields retained");
                parameter.SavedOffsets[0] = 9999;
                Need(EditReadCatalogue.Parameters(d, "99.07").Parameters[0].SavedOffsets[0] == firstOffset, "No shared mutable profile arrays");
                Reject<ArgumentException>(() => EditReadCatalogue.Capabilities(d, "99.7"));
                Reject<ArgumentException>(() => EditReadCatalogue.Capabilities(d, "99.07\n"));
                Reject<ArgumentException>(() => EditReadCatalogue.Capabilities(d with { AmpColumns = 1 }, "99.07"));
                Reject<ArgumentException>(() => EditReadCatalogue.Parameters(d, "99.07", " "));
            });
            yield return ($"{d.Model}/{d.PresetCapacity} exact prefixes survive reordered and same-shape records", () =>
            {
                var f = Build(d);
                byte[] wire = Fixtures.FromBody(d, d.PresetCapacity - 1, f.Body).Sysex, original = wire.ToArray();
                var report = PresetEditProjection.Decode(d, "99.07", wire);
                Need(wire.SequenceEqual(original), "Input capture unchanged");
                Need(Convert.FromHexString(report.BodyHex).SequenceEqual(f.Body), "All body bytes retained");
                Need(report.Slot == d.PresetCapacity - 1 && report.Status == "partial" && !report.ExecutionSupported, "Explicit scope/partial status");
                foreach (var id in d.Model == "FM3" ? new[] { 58 } : new[] { 58, 59 })
                {
                    var block = report.Blocks.Single(b => b.EffectId == id);
                    Need(block.IdentityStatus == "exact-prefix-match" && block.Record!.ShapeOffset == f.Offsets[id], "Exact record, not sorted shape");
                    Need(block.Parameters.Where(p => p.Key == "amp.type").Select(p => p.RawValue).SequenceEqual(Enumerable.Range(id * 10, 4)), "Independent expected Amp IDs");
                    Need(block.Parameters.Where(p => p.Key == "amp.type").Select(p => p.Channel).SequenceEqual(new[] { "A", "B", "C", "D" }), "All channels");
                    Need(block.Scenes.Length == 8 && block.Scenes[0].Channel == "D" && block.Scenes[7].Channel == "A", "First scene preceding shape and last scene");
                    Need(block.Scenes[0].Bypassed == true && block.Scenes[7].Bypassed == false, "Bypass values");
                }
                var cab = report.Blocks.Single(b => b.EffectId == 62);
                Need(cab.Identity.Name == "Cab 1" && cab.ParameterStatus == "unsupported-record-shape" && cab.Parameters.Length == 0, "Same-shaped Cab never interpreted as Amp");
                var unknown = report.Blocks.Single(b => b.EffectId == 900);
                Need(unknown.Identity.Name == null && unknown.Record != null && unknown.Parameters.Length == 0, "Unknown identity remains numeric with exact record");
                Need(report.SceneNames[0].Text == "Synthetic clean" && report.SceneNames[7].Text == "Synthetic lead", "Independent name values");
                Need(report.Routing.Edges is { Length: 0 } && report.Routing.Words[0].RawWord == 0x8005 && report.Routing.Words[0].UnknownBits == 0x8000, "No fabricated edges or lost bits");
                Need(report.Routing.Words.All(w => w.KnownMask == (d.Model == "FM3" ? 15 : 63)), "Model-specific route mask");
                Need(report.Routing.Nodes.Any(n => n.Kind == "shunt" && n.EffectId == 1025), "Shunt retained distinctly");
                Need(Convert.FromHexString(report.Routing.RawGridHex).SequenceEqual(f.Body.AsSpan(0x104, d.GridRows * d.GridColumns * 4).ToArray()), "Lossless grid");
                Need(report.SysexSha256 == PresetBody.Sha256(wire) && report.BodySha256 == PresetBody.Sha256(f.Body), "Hash bindings");
                Need(SnapshotExport.Json(report) == SnapshotExport.Json(PresetEditProjection.Decode(d, "99.07", wire)), "Deterministic projection");
                if (destination != null)
                {
                    string folder = Path.Combine(destination, $"Edit-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, "synthetic.syx"), wire);
                }
            });
            yield return ($"{d.Model}/{d.PresetCapacity} missing duplicate and foreign records never fall back to shape", () =>
            {
                var f = Build(d);
                void Check(byte[] body, string status)
                {
                    var report = Decode(d, body);
                    var block = report.Blocks.Single(b => b.EffectId == 58);
                    Need(block.IdentityStatus == status && block.Record == null && block.Parameters.Length == 0 && block.Scenes.Length == 0, status);
                    Need(report.Issues.Any(i => i.Code == status && i.EffectId == 58), "Actionable issue");
                    Need(report.SceneNames[0].Text == "Synthetic clean", "Other reads remain available");
                }
                var body = f.Body.ToArray(); Put(body, f.Offsets[58] - 12, 901); Check(body, "missing-instance-record");
                body = f.Body.ToArray(); Put(body, f.Offsets[62] - 12, 58); Check(body, "ambiguous-instance-record");
                body = f.Body.ToArray(); Put(body, 0x104 + (d.GridRows * d.GridColumns - 1) * 4, 58); Check(body, "duplicate-grid-instance");
            });
            yield return ($"{d.Model}/{d.PresetCapacity} unsupported semantics preserve raw values and names", () =>
            {
                var f = Build(d);
                byte[] body = f.Body.ToArray();
                body[4] = 0x80; // must not become '?'
                Put(body, f.Offsets[58] - 2, 7); Put(body, f.Offsets[58] + 14, 2);
                var report = Decode(d, body);
                Need(report.SceneNames[0].Text == null && report.SceneNames[0].RawHex.StartsWith("80"), "Unknown text retained");
                var scene = report.Blocks.Single(b => b.EffectId == 58).Scenes[0];
                Need(scene.Channel == null && scene.Bypassed == null && scene.RawChannel == 7 && scene.RawBypass == 2, "No coercion");
                Need(report.Blocks.Single(b => b.EffectId == 58).Parameters.Count(p => p.Key == "amp.type") == 4, "Independent parameter reads preserved");
                body = f.Body.ToArray(); Array.Fill(body, (byte)'Z', 4, 32); body[36] = 0; body[37] = 255;
                report = Decode(d, body);
                Need(report.SceneNames[0].Text == new string('Z', 32) && report.SceneNames[1].Text == "", "Full-width and empty names");
                Need(report.SceneNames[1].RawHex.StartsWith("00ff"), "Opaque name padding retained");
                body = f.Body.ToArray(); Put(body, 0x400 + 30, 26); // missing anchor pair
                report = Decode(d, body);
                Need(report.Blocks.Single(b => b.EffectId == 58).IdentityStatus == "missing-instance-record", "No guessed record scan");
                body = f.Body.ToArray(); Put(body, f.Offsets[58] + 30, 501);
                report = Decode(d, body);
                Need(report.Issues.Any(i => i.Code == "unsupported-body-layout") && Convert.FromHexString(report.BodyHex).SequenceEqual(body), "Unsupported body retained fully");
                var wrongShape = Build(d, wrongAmpShape: true);
                report = Decode(d, wrongShape.Body);
                Need(report.Blocks.Single(b => b.EffectId == 58).ParameterStatus == "unsupported-record-shape", "Exact prefix doesn't imply layout support");
            });
            yield return ($"{d.Model}/{d.PresetCapacity} edit projection enforces capture integrity and addressing", () =>
            {
                var f = Build(d);
                byte[] wire = Fixtures.FromBody(d, 0, f.Body).Sysex;
                var bad = wire.ToArray(); bad[100] ^= 1;
                Reject<InvalidDataException>(() => PresetEditProjection.Decode(d, "99.07", bad));
                Reject<InvalidDataException>(() => PresetEditProjection.Decode(d, "99.07", wire[..^1]));
                var other = DeviceDefinition.Create(d.Model == "FM9" ? "FM3" : "FM9");
                Reject<InvalidDataException>(() => PresetEditProjection.Decode(other, "99.07", wire));
                var frames = Fixtures.Frames(wire);
                frames[0] = SysexProtocol.Frame(d.ModelByte, 0x77, 127, 0, 0, 64, 0);
                Reject<InvalidDataException>(() => PresetEditProjection.Decode(d, "99.07", frames.SelectMany(b => b).ToArray()));
                Reject<ArgumentException>(() => PresetEditProjection.Decode(d, "12.0", wire));
            });
        }
    }
    private static PresetEditReadReport Decode(DeviceDefinition d, byte[] body) =>
        PresetEditProjection.Decode(d, "99.07", Fixtures.FromBody(d, 0, body).Sysex);
    private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)value);

    private static (byte[] Body, Dictionary<int, int> Offsets) Build(DeviceDefinition d, bool wrongAmpShape = false)
    {
        (int rows, int columns, int ampColumns, int[] typeOffsets) = d.Model switch
        {
            "FM9" => (6, 14, 147, new[] { 54, 348, 642, 936 }),
            "FM3" => (4, 12, 144, new[] { 46, 334, 622, 910 }),
            _ => (6, 14, 142, new[] { 34, 318, 602, 886 })
        };
        byte[] body = new byte[7200];
        for (int scene = 0; scene < 8; scene++) Encoding.ASCII.GetBytes(scene == 0 ? "Synthetic clean" : scene == 7 ? "Synthetic lead" : $"Synthetic {scene}").CopyTo(body, 4 + scene * 32);
        Put(body, 0, 2); Put(body, 2, 0xabcd);
        int pos = 0x400;
        var offsets = new Dictionary<int, int>();
        void Record(int id, int cols, int channels)
        {
            offsets[id] = pos;
            Put(body, pos - 12, id); Put(body, pos + 30, cols); Put(body, pos + 32, channels);
            pos += (23 + cols * channels) * 2;
        }
        Record(2, 25, 1); Record(35, 25, 1);
        int[] ids = d.Model == "FM3" ? [62, 58, 900] : [59, 62, 58, 900];
        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            // Same-shaped Cab decoy and reverse Amp order expose shape/order matching bugs.
            Record(id, id == 900 ? 3 : id == 58 && wrongAmpShape ? ampColumns + 1 : ampColumns, id == 900 ? 1 : 4);
            int start = offsets[id];
            Put(body, 0x104 + i * rows * 4, id);
            if (id is 58 or 59 or 62)
            {
                for (int ch = 0; ch < 4; ch++) Put(body, start + typeOffsets[ch], id * 10 + ch);
                for (int scene = 0; scene < 8; scene++)
                { Put(body, start + (scene - 1) * 2, 3 - scene % 4); Put(body, start + 14 + scene * 2, 1 - scene % 2); }
            }
        }
        Put(body, 0x108, 1025);
        Put(body, 0x106, 0x8005);
        Put(body, 0x104 + (rows * columns - 1) * 4 + 2, 0x40);
        body[^1] = 0x9a; // unrelated unparsed tail must survive
        return (body, offsets);
    }
}
