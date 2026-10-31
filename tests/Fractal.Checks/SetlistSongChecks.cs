using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Fractal.Core;

static class SetlistSongChecks
{
    public static async Task Run(DeviceDefinition d, string? output)
    {
        var context = new DeviceContext(d, "99.07", "explicit-synthetic-context");
        SavedCapture Capture(int slot, int scene)
        {
            var initial = Fixtures.Preset(d, slot);
            byte[] body = initial.Image.Body.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)(scene - 1));
            return Fixtures.FromBody(d, slot, body, "Synthetic");
        }
        var captures = new[] { Capture(0, 6), Capture(d.PresetCapacity - 1, 2) };
        byte[][] originals = captures.Select(c => c.Sysex.ToArray()).ToArray();
        int[] slots = [0, d.PresetCapacity - 1];
        var request = new SetlistSongRequest(1, d.Model, d.PresetCapacity, context.Firmware,
            [new(1, "Song One", [new(1, "Intro", 0, DefaultScene: true), new(3, "Solo", slots[1], Scene: 8), new(6, "Unused")])],
            [new(1, "Set One", [1, 1])], 1, true);
        JsonElement Plan(SetlistSongRequest r, SavedCapture[]? saved = null, DeviceContext? c = null) =>
            JsonSerializer.SerializeToElement(SetlistSongs.Plan(r, c ?? context, saved ?? captures, slots, true), SetlistSongs.JsonOptions);
        var plan = Plan(request);
        Need(plan.GetProperty("dependencies_resolved").GetBoolean(), "All retained references resolved");
        Need(!plan.GetProperty("device_executable").GetBoolean() && !plan.GetProperty("editor_importable").GetBoolean() &&
            !plan.GetProperty("ports_opened").GetBoolean(), "Proposal remains offline with no interchange/execution claim");
        var section = plan.GetProperty("songs")[0].GetProperty("sections")[0];
        Need(section.GetProperty("scene_selection").GetString() == "default" && section.GetProperty("resolved_scene").GetInt32() == 6,
            "DEFAULT is symbolic with an independent saved-word preview");
        Need(plan.GetProperty("songs")[0].GetProperty("sections")[1].GetProperty("resolved_scene").GetInt32() == 8, "Explicit scene remains exact");
        Need(plan.GetProperty("songs")[0].GetProperty("sections")[2].GetProperty("status").GetString() == "disabled", "Disabled section retained");
        var positions = plan.GetProperty("setlists")[0].GetProperty("songs");
        Need(positions.GetArrayLength() == 2 && positions[1].GetProperty("position").GetInt32() == 2 && positions[1].GetProperty("song").GetInt32() == 1,
            "Repeated songs retain ordered set positions");
        Need(!plan.GetProperty("inventory").GetProperty("covers_full_capacity").GetBoolean(), "Selected inventory is not full coverage");
        Need(plan.GetRawText() == Plan(request).GetRawText(), "Repeated offline planning is deterministic");
        for (int i = 0; i < captures.Length; i++) Need(originals[i].SequenceEqual(captures[i].Sysex), "Input saved bytes preserved");

        var missing = request with { Songs = [new(1, "Song One", [new(2, "Verse", 1, Scene: 3)])],
            Setlists = [new(2, "Set Two", [1, 128, 1])], ActiveSetlist = 4, FcControllerPresent = null };
        var unresolved = Plan(missing).GetProperty("unresolved_dependencies").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToArray();
        Need(unresolved.Contains("preset-not-in-inventory") && unresolved.Contains("song-definition-missing") &&
            unresolved.Contains("active-setlist-definition-missing"), "Missing references remain visible with their locations");
        Need(unresolved.Contains("fc-controller-unconfirmed") == (d.Model == "Axe-Fx III"), "FC requirement is model-specific");
        if (d.Model == "Axe-Fx III") Need(Plan(request with { FcControllerPresent = false }).GetProperty("unresolved_dependencies")[0]
            .GetProperty("code").GetString() == "fc-controller-missing", "Absent FC is a dependency, not claimed hardware support");
        var partial = JsonSerializer.SerializeToElement(SetlistSongs.Plan(request, context, [], slots, false), SetlistSongs.JsonOptions);
        Need(!partial.GetProperty("dependencies_resolved").GetBoolean() && !partial.GetProperty("inventory").GetProperty("capture_complete").GetBoolean(),
            "Partial/empty retained inventory reports unresolved dependencies");
        string minimum = d.Model switch { "FM9" => "3.00", "FM3" => "6.00", _ => "19.06" };
        _ = Plan(request with { Firmware = minimum }, c: context with { Firmware = minimum });
        string below = d.Model switch { "FM9" => "2.99", "FM3" => "5.99", _ => "19.05" };
        Reject<InvalidDataException>(() => Plan(request with { Firmware = below }, c: context with { Firmware = below }));
        Reject<InvalidDataException>(() => Plan(request with { Firmware = "unknown" }));
        Reject<InvalidDataException>(() => Plan(request with { Firmware = "99.08" }));
        Reject<InvalidDataException>(() => Plan(request with { Songs = [request.Songs[0], request.Songs[0]] }));
        Reject<InvalidDataException>(() => Plan(request with { Setlists = [request.Setlists[0], request.Setlists[0]] }));
        Reject<InvalidDataException>(() => Plan(request with { Setlists = [new(1, "Too Long!!!", [1])] }));
        Reject<InvalidDataException>(() => Plan(request with { Setlists = [new(1, "Bad", [0])] }));
        Reject<InvalidDataException>(() => Plan(request with { Setlists = [new(1, "Bad", Enumerable.Repeat(1, 33).ToArray())] }));
        foreach (var bad in new[] { new SongSection(1, "Bad", 0), new(1, "Bad", 0, 1, true), new(1, "Bad", 0, 1, false),
            new(1, "Bad", Scene: 1), new(7, "Bad"), new(1, "Bad\n"), new(1, "Bad "), new(1, "Bad\u00e9"), new(1, "Bad", 0, 0) })
            Reject<InvalidDataException>(() => Plan(request with { Songs = [new(1, "Song", [bad])] }));
        Reject<ArgumentOutOfRangeException>(() => Plan(request with { Songs = [new(1, "Song", [new(1, "Bad", d.PresetCapacity, 1)])] }));
        Reject<InvalidDataException>(() => Plan(request with { Songs = [new(1, "Song", [new(1, "A"), new(1, "B")])] }));
        // Manufacturer maximums and all set positions are exercised without requiring a complete inventory.
        var maximum = request with { Songs = Enumerable.Range(1, 128).Select(n => new SongDefinition(n, "Song", Enumerable.Range(1, 6)
            .Select(s => new SongSection(s, "Section", 0, s)).ToArray())).ToArray(), Setlists = Enumerable.Range(1, 4)
            .Select(n => new SetlistDefinition(n, "Set", Enumerable.Range(1, 32).ToArray())).ToArray() };
        Need(Plan(maximum).GetProperty("dependencies_resolved").GetBoolean(), "128 songs, six sections, four sets and 32 positions accepted");
        Reject<InvalidDataException>(() => Plan(maximum with { Songs = [.. maximum.Songs, new(129, "Overflow", [])] }));
        var corrupt = captures.Select(c => new SavedCapture(c.Image, c.Sysex.ToArray())).ToArray(); corrupt[0].Sysex[20] ^= 1;
        Reject<InvalidDataException>(() => Plan(request, corrupt));
        Reject<InvalidDataException>(() => SetlistSongs.Parse(Encoding.UTF8.GetBytes("{\"schema_version\":1,\"schema_version\":1}")));
        Reject<InvalidDataException>(() => SetlistSongs.Parse(Encoding.UTF8.GetBytes("{\"schema_version\":1}")));
        string encoded = JsonSerializer.Serialize(request, SetlistSongs.JsonOptions);
        Reject<InvalidDataException>(() => SetlistSongs.Parse(Encoding.UTF8.GetBytes(encoded.Replace("\"schema_version\": 1", "\"unknown\": 1"))));

        if (output != null)
        {
            string root = Path.Combine(output, $"Setlists-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(root);
            using var store = PresetInventory.Create(Path.Combine(root, "inventory"), context, slots);
            var midi = new FakeMidi { Reply = (q, m) =>
            {
                if (q[5] == 0x0d) m.Emit(SysexProtocol.Frame(d.ModelByte, 0x0d, new byte[34]));
                else if (q[5] == 0x0c) m.Emit(SysexProtocol.Frame(d.ModelByte, 0x0c, 0));
                else if (q[5] == 3) m.Fragment(captures.Single(c => c.Image.Slot == (q[6] << 7 | q[7])).Sysex);
                else throw new Exception("Unexpected synthetic inventory request");
            } };
            await using var reader = new ReadSession(midi);
            await store.RunAsync(reader, context, TimeSpan.FromMilliseconds(500), default);
            File.WriteAllText(Path.Combine(root, "request.json"), encoded);
            _ = SetlistSongs.Write(SetlistSongs.Plan(request, store), Path.Combine(root, "proposal"));
            Reject<IOException>(() => SetlistSongs.Write(plan, Path.Combine(root, "proposal")));
        }
    }
    private static void Need(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
