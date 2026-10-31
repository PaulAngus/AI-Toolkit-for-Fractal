using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Buffers.Binary;
using Fractal.Core;

static class InventoryAnalysisChecks
{
    public static async Task Run(DeviceDefinition d)
    {
        var context = new DeviceContext(d, "99.07", "synthetic");
        byte[] Roster(object value) => JsonSerializer.SerializeToUtf8Bytes(value);
        Need(InventoryAnalysis.ReadRoster(Roster(new { model = d.Model, firmware = "99.07", entries = new[] { new { id = 60000, name = "Synthetic" } } }), context).SequenceEqual([60000]), "Contained catalogue entry format is usable without conversion");
        foreach (byte[] malformed in new[] { Roster(new { model = d.Model, firmware = "99.07" }),
            Roster(new { model = d.Model, firmware = "99.07", ids = new[] { 1, 1 } }),
            Roster(new { model = d.Model, firmware = "99.08", ids = new[] { 1 } }), "{"u8.ToArray() })
        {
            bool refused = false; try { InventoryAnalysis.ReadRoster(malformed, context); } catch (InvalidDataException) { refused = true; }
            Need(refused, "Malformed or incompatible roster is a structured refusal");
        }
        string root = Path.Combine(Path.GetTempPath(), "fractal-analysis-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, SnapshotExport.JsonOptions);
        async Task<PresetInventory> Scan(string name, SavedCapture[] captures)
        {
            var store = PresetInventory.Create(Path.Combine(root, name), context, captures.Select(c => c.Image.Slot));
            var midi = new FakeMidi { Reply = (q, m) =>
            {
                if (q[5] == 0x0d) m.Emit(SysexProtocol.Frame(d.ModelByte, 0x0d, new byte[34]));
                else if (q[5] == 0x0c) m.Emit(SysexProtocol.Frame(d.ModelByte, 0x0c, 0));
                else m.Emit(captures.Single(c => c.Image.Slot == (q[6] << 7 | q[7])).Sysex);
            } };
            await using var session = new ReadSession(midi);
            try { await store.RunAsync(session, context, TimeSpan.FromSeconds(1), default); return store; }
            catch { store.Dispose(); throw; }
        }
        try
        {
            byte[] body = AudioRoutingChecks.Build(d).Body; byte[] changed = body.ToArray(); changed[^1] ^= 1;
            using var before = await Scan("before", [Fixtures.FromBody(d, 0, body), Fixtures.FromBody(d, 1, changed)]);
            using var after = await Scan("after", [Fixtures.FromBody(d, 0, changed), Fixtures.FromBody(d, 1, body, "Renamed")]);
            var comparison = Json(InventoryAnalysis.Compare(before, after));
            Need(comparison.GetProperty("content_matches").EnumerateArray().All(c => c.GetProperty("moved_candidate").GetBoolean()), "Unique whole-body swap correspondence");
            Need(!comparison.GetProperty("full_capacity_comparison").GetBoolean() && !comparison.GetProperty("all_saved_raw_equal").GetBoolean(), "Subset is not full inventory proof");
            using var rename = await Scan("rename", [Fixtures.FromBody(d, 0, body, "Renamed")]);
            var renamed = Json(InventoryAnalysis.Compare(before, rename));
            Need(renamed.GetProperty("changes")[0].GetProperty("status").GetString() == "preset-name-change", "Only name ignored in name-independent match");
            Need(renamed.GetProperty("changes")[1].GetProperty("status").GetString() == "unknown-missing-capture", "Missing evidence not deletion");
            byte[] sceneBase = AudioRoutingChecks.Build(d).Body;
            for (int s = 0; s < 8; s++)
            {
                sceneBase.AsSpan(4 + 32 * s, 32).Clear();
                Encoding.ASCII.GetBytes("SCENE-" + (s + 1)).CopyTo(sceneBase.AsSpan(4 + 32 * s));
            }
            var sceneRead = PresetEditProjection.Decode(d, "99.07", Fixtures.FromBody(d, 0, sceneBase).Sysex);
            foreach (var block in sceneRead.Blocks)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(sceneBase.AsSpan(block.Scenes[1].ChannelOffset, 2), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(sceneBase.AsSpan(block.Scenes[1].BypassOffset, 2), 1);
            }
            sceneRead = PresetEditProjection.Decode(d, "99.07", Fixtures.FromBody(d, 0, sceneBase).Sysex);
            void Swap(byte[] bytes, int one, int two, int length)
            {
                byte[] temp = bytes.AsSpan(one, length).ToArray();
                bytes.AsSpan(two, length).CopyTo(bytes.AsSpan(one, length));
                temp.CopyTo(bytes.AsSpan(two, length));
            }
            byte[] swappedBody = sceneBase.ToArray();
            Swap(swappedBody, 4, 36, 32);
            foreach (var block in sceneRead.Blocks)
            {
                Need(block.Scenes.Length == 8, "Synthetic placed block has complete scene words");
                Swap(swappedBody, block.Scenes[0].ChannelOffset, block.Scenes[1].ChannelOffset, 2);
                Swap(swappedBody, block.Scenes[0].BypassOffset, block.Scenes[1].BypassOffset, 2);
            }
            using var sceneBefore = await Scan("scene-before", [Fixtures.FromBody(d, 0, sceneBase)]);
            using var sceneAfter = await Scan("scene-after", [Fixtures.FromBody(d, 0, swappedBody)]);
            var sceneChange = Json(InventoryAnalysis.Compare(sceneBefore, sceneAfter)).GetProperty("changes")[0].GetProperty("scene_permutation");
            Need(sceneChange.GetProperty("status").GetString() == "exact-known-field-permutation" &&
                sceneChange.GetProperty("before_to_after").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { 2, 1, 3, 4, 5, 6, 7, 8 }),
                "Name and every known block scene field swap with unchanged remainder is recognized exactly");
            byte[] otherChanged = swappedBody.ToArray(); otherChanged[^1] ^= 1;
            var incomplete = ScenePermutationAnalysis.Compare(d, "99.07", Fixtures.FromBody(d, 0, sceneBase).Sysex, Fixtures.FromBody(d, 0, otherChanged).Sysex);
            Need(incomplete.Status == "unexplained-body-change" && incomplete.BeforeToAfter == null,
                "Unrelated opaque change prevents scene permutation claim");
            byte[] midiBase = sceneBase.ToArray();
            int midiOffset = sceneRead.Records.Max(x => x.ShapeOffset + x.ByteLength);
            void Put(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(midiBase.AsSpan(offset, 2), (ushort)value);
            Put(midiOffset - 12, 190); Put(midiOffset + 30, 257); Put(midiOffset + 32, 1);
            Put(0x104 + 5 * d.GridRows * 4, 190);
            for (int s = 0; s < 8; s++)
            {
                Put(midiOffset + 2 * (17 + 8 * s), 1);
                Put(midiOffset + 2 * (17 + 128 + 8 * s), 1);
                Put(midiOffset + 2 * (17 + 192 + 8 * s), 10 + s);
            }
            byte[] midiSwapped = midiBase.ToArray(); Swap(midiSwapped, 4, 36, 32);
            foreach (var block in sceneRead.Blocks)
            {
                Swap(midiSwapped, block.Scenes[0].ChannelOffset, block.Scenes[1].ChannelOffset, 2);
                Swap(midiSwapped, block.Scenes[0].BypassOffset, block.Scenes[1].BypassOffset, 2);
            }
            foreach (int bank in new[] { 0, 64, 128, 192 })
                Swap(midiSwapped, midiOffset + 2 * (17 + bank), midiOffset + 2 * (17 + bank + 8), 16);
            var midiPermutation = ScenePermutationAnalysis.Compare(d, "99.07", Fixtures.FromBody(d, 0, midiBase).Sysex, Fixtures.FromBody(d, 0, midiSwapped).Sysex);
            Need(midiPermutation.Status == "exact-known-field-permutation" && midiPermutation.BeforeToAfter![0] == 2,
                "Complete Scene MIDI banks participate in the byte-exact scene swap proof");
            byte[] ambiguousBase = sceneBase.ToArray(); ambiguousBase.AsSpan(36, 32).Clear(); ambiguousBase.AsSpan(4, 32).CopyTo(ambiguousBase.AsSpan(36, 32));
            foreach (var block in sceneRead.Blocks)
            {
                ambiguousBase.AsSpan(block.Scenes[0].ChannelOffset, 2).CopyTo(ambiguousBase.AsSpan(block.Scenes[1].ChannelOffset, 2));
                ambiguousBase.AsSpan(block.Scenes[0].BypassOffset, 2).CopyTo(ambiguousBase.AsSpan(block.Scenes[1].BypassOffset, 2));
            }
            byte[] ambiguousAfter = ambiguousBase.ToArray(); Swap(ambiguousAfter, 4, 68, 32);
            foreach (var block in sceneRead.Blocks)
            {
                Swap(ambiguousAfter, block.Scenes[0].ChannelOffset, block.Scenes[2].ChannelOffset, 2);
                Swap(ambiguousAfter, block.Scenes[0].BypassOffset, block.Scenes[2].BypassOffset, 2);
            }
            var ambiguous = ScenePermutationAnalysis.Compare(d, "99.07", Fixtures.FromBody(d, 0, ambiguousBase).Sysex, Fixtures.FromBody(d, 0, ambiguousAfter).Sysex);
            Need(ambiguous.Status == "ambiguous-identical-scenes" && ambiguous.BeforeToAfter == null,
                "Indistinguishable scenes never receive invented correspondence");
            using var duplicate = await Scan("duplicate", [Fixtures.FromBody(d, 0, body), Fixtures.FromBody(d, 1, body)]);
            Need(Json(InventoryAnalysis.Compare(duplicate, rename)).GetProperty("content_matches")[0].GetProperty("status").GetString() == "ambiguous-identical-content", "Copies never infer unique identity");
            var usage = Json(InventoryAnalysis.AmpUsage(before, 60000, [60000, 65535]));
            Need(usage.GetProperty("uses").GetArrayLength() == 2 && usage.GetProperty("uses")[0].GetProperty("channel").GetString() == "A", "Saved exact-instance/channel amp search");
            Need(usage.GetProperty("absent_ids").ValueKind == JsonValueKind.Null && usage.GetProperty("absence_status").GetString() == "unknown-incomplete-coverage", "No false absence from selected slots");
            var self = Json(InventoryAnalysis.Compare(before, before));
            Need(self.GetProperty("changes").EnumerateArray().All(c => c.GetProperty("status").GetString() == "identical-raw"), "Self comparison exact raw equality");
            using var empty = PresetInventory.Create(Path.Combine(root, "empty"), context, [0]);
            Need(Json(InventoryAnalysis.AmpUsage(empty)).GetProperty("missing_slots").GetArrayLength() == 1, "Unattempted slots visible in search");
            using var full = await Scan("full", Enumerable.Range(0, d.PresetCapacity).Select(slot => Fixtures.FromBody(d, slot, body)).ToArray());
            var fullUsage = Json(InventoryAnalysis.AmpUsage(full, null, [60000, 65535]));
            Need(fullUsage.GetProperty("full_capacity_decoded").GetBoolean() && fullUsage.GetProperty("absent_ids").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual([65535]), "Full decoded coverage permits exact roster set difference");
            Need(Json(InventoryAnalysis.Compare(full, full)).GetProperty("all_saved_raw_equal").GetBoolean(), "Complete synthetic capacity proves raw equality");
            var page = Json(full.Inspect(1, 2));
            Need(page.GetProperty("entries").GetArrayLength() == 2 && page.GetProperty("next_offset").GetInt32() == 3 && page.GetProperty("capture_complete").GetBoolean(), "Paged inspection preserves whole-inventory completeness");
            var usagePage = Json(InventoryAnalysis.AmpUsage(full, null, [65535], 1, 2));
            Need(usagePage.GetProperty("uses").GetArrayLength() == 2 && usagePage.GetProperty("absent_ids")[0].GetInt32() == 65535, "Usage pagination does not truncate absence calculation");
        }
        finally { Directory.Delete(root, true); }
        Directory.CreateDirectory(root);
        try
        {
            using (var old = await Scan("old", [Fixtures.FromBody(d, 0, AudioRoutingChecks.Build(d).Body)])) { }
            string path = Path.Combine(root, "old", "inventory.json"); var checkpoint = JsonNode.Parse(File.ReadAllBytes(path))!;
            checkpoint["protocol_release"] = "0.15.0"; File.WriteAllText(path, checkpoint.ToJsonString());
            byte[] original = File.ReadAllBytes(path);
            PresetInventory.Migrate(Path.Combine(root, "old"), Path.Combine(root, "migrated"));
            using var migrated = PresetInventory.Open(Path.Combine(root, "migrated"));
            Need(migrated.Complete && File.ReadAllBytes(path).SequenceEqual(original), "Migration preserves original and verified completion");
            checkpoint["protocol_release"] = "0.17.0"; File.WriteAllText(path, checkpoint.ToJsonString());
            PresetInventory.Migrate(Path.Combine(root, "old"), Path.Combine(root, "migrated-17"));
            using var recent = PresetInventory.Open(Path.Combine(root, "migrated-17"));
            Need(recent.Complete, "Most recent schema-compatible release migrates explicitly");
            checkpoint["protocol_release"] = "0.24.0"; File.WriteAllText(path, checkpoint.ToJsonString());
            PresetInventory.Migrate(Path.Combine(root, "old"), Path.Combine(root, "migrated-24"));
            using var previous = PresetInventory.Open(Path.Combine(root, "migrated-24"));
            Need(previous.Complete, "Immediate prior schema-compatible release migrates explicitly");
        }
        finally { Directory.Delete(root, true); }
    }
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
}
