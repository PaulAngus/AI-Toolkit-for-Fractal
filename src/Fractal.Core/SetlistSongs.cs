using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Fractal.Core;

public sealed record SongSection([property: JsonRequired] int Number, [property: JsonRequired] string Name,
    int? PresetSlot = null, int? Scene = null, bool? DefaultScene = null);
public sealed record SongDefinition([property: JsonRequired] int Number, [property: JsonRequired] string Name,
    [property: JsonRequired] SongSection[] Sections);
public sealed record SetlistDefinition([property: JsonRequired] int Number, [property: JsonRequired] string Name,
    [property: JsonRequired] int[] Songs);
public sealed record SetlistSongRequest([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string Model,
    [property: JsonRequired] int Capacity, [property: JsonRequired] string Firmware,
    [property: JsonRequired] SongDefinition[] Songs, [property: JsonRequired] SetlistDefinition[] Setlists,
    int? ActiveSetlist = null, bool? FcControllerPresent = null);
public sealed record SongDependency(string Code, int? Song, int? Section, int? Setlist, int? Position, int? PresetSlot);
public sealed record SongSectionPreview(int Number, string Name, int? PresetSlot, string SceneSelection,
    int? RequestedScene, string Status, int? ResolvedScene, string? PresetName, string? SceneName, string? CaptureSha256);
public sealed record SongPreview(int Number, string Name, SongSectionPreview[] Sections);
public sealed record SetlistPositionPreview(int Position, int Song, string? Name, string Status);
public sealed record SetlistPreview(int Number, string Name, bool Active, SetlistPositionPreview[] Songs);
public sealed record SongPresetEvidence(int Slot, string SysexSha256, string RawSha256, string BodySha256, int SavedDefaultScene);

/// <summary>Toolkit-native offline proposals, never a system-data patch or executable device format.</summary>
public static class SetlistSongs
{
    public static readonly JsonSerializerOptions JsonOptions = new(PresetEditPlan.RequestJson)
        { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public const string SourceId = "fractal-setlists-songs-manual";

    public static SetlistSongRequest Parse(byte[] json)
    {
        if (json.Length > 512_000) throw new InvalidDataException("Setlist/song request exceeds size bound.");
        try
        {
            using var document = JsonDocument.Parse(json);
            NoDuplicateMembers(document.RootElement);
            return JsonSerializer.Deserialize<SetlistSongRequest>(json, JsonOptions)
                ?? throw new InvalidDataException("Missing setlist/song request.");
        }
        catch (JsonException e) { throw new InvalidDataException("Malformed or unsupported setlist/song request.", e); }
    }

    public static object Plan(SetlistSongRequest input, PresetInventory inventory) => Plan(input, inventory.Context,
        inventory.ReadCaptures(), inventory.RequestedSlots, inventory.Complete);

    public static object Plan(SetlistSongRequest input, DeviceContext context, SavedCapture[] captures,
        int[] requestedSlots, bool inventoryCompleted)
    {
        // Freeze caller-owned arrays and reject unknown JSON properties at the public parsing boundary.
        var request = Parse(JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions));
        var d = DeviceDefinition.Create(request.Model, request.Capacity);
        if (request.SchemaVersion != 1 || request.Firmware == null || !Regex.IsMatch(request.Firmware, @"^\d{1,3}\.\d{2}$"))
            throw new InvalidDataException("Use schema 1 and an explicit major.minor firmware version.");
        string minimum = d.Model switch { "FM9" => "3.00", "FM3" => "6.00", "Axe-Fx III" => "19.06", _ => throw new InvalidDataException() };
        if (Version.Parse(request.Firmware) < Version.Parse(minimum))
            throw new InvalidDataException("Firmware predates the manufacturer's documented setlist/song minimum for this model.");
        if (context.Definition != d || context.Firmware != request.Firmware)
            throw new InvalidDataException("Request model, capacity and firmware must match the supplied inventory context.");
        if (request.Songs == null || request.Setlists == null || request.Songs.Length > 128 || request.Setlists.Length > 4 ||
            request.Songs.Length + request.Setlists.Length == 0)
            throw new InvalidDataException("Supply bounded song/setlist arrays with at least one proposed record.");
        DistinctNumbers(request.Songs.Select(s => s?.Number), 128, "song");
        DistinctNumbers(request.Setlists.Select(s => s?.Number), 4, "setlist");
        if (request.ActiveSetlist is < 1 or > 4) throw new InvalidDataException("Active setlist must be 1–4, when specified.");
        if (captures == null || requestedSlots == null || requestedSlots.Length > d.PresetCapacity ||
            captures.Length > d.PresetCapacity || requestedSlots.Distinct().Count() != requestedSlots.Length)
            throw new InvalidDataException("Inventory must contain distinct bounded requested slots.");
        foreach (int slot in requestedSlots) d.ValidateSlot(slot);
        var saved = new Dictionary<int, SavedCapture>();
        foreach (var capture in captures)
        {
            var checkedCapture = SnapshotExport.DecodeFile(d, capture.Sysex);
            if (checkedCapture.Image.Slot != capture.Image.Slot || !checkedCapture.Image.RawImage.SequenceEqual(capture.Image.RawImage) ||
                !checkedCapture.Image.Body.SequenceEqual(capture.Image.Body) || !requestedSlots.Contains(checkedCapture.Image.Slot) ||
                !saved.TryAdd(checkedCapture.Image.Slot, checkedCapture))
                throw new InvalidDataException("Inventory capture, addressing, requested slots or integrity disagree.");
        }
        if (inventoryCompleted && saved.Count != requestedSlots.Length)
            throw new InvalidDataException("Completed inventory is missing a requested capture.");

        List<SongDependency> unresolved = [];
        if (d.Model == "Axe-Fx III" && request.FcControllerPresent != true)
            unresolved.Add(new(request.FcControllerPresent == false ? "fc-controller-missing" : "fc-controller-unconfirmed", null, null, null, null, null));
        var songMap = request.Songs.ToDictionary(s => s.Number);
        List<SongPreview> songs = [];
        var referenced = new SortedDictionary<int, SongPresetEvidence>();
        foreach (var song in request.Songs.OrderBy(s => s.Number))
        {
            Name(song.Name);
            if (song.Sections == null || song.Sections.Length > 6) throw new InvalidDataException("A song has at most six numbered sections.");
            DistinctNumbers(song.Sections.Select(s => s?.Number), 6, "section");
            List<SongSectionPreview> sections = [];
            foreach (var section in song.Sections.OrderBy(s => s.Number))
            {
                Name(section.Name);
                if (section.PresetSlot == null)
                {
                    if (section.Scene != null || section.DefaultScene != null)
                        throw new InvalidDataException("Disabled sections omit preset_slot, scene and default_scene.");
                    sections.Add(new(section.Number, section.Name, null, "disabled", null, "disabled", null, null, null, null));
                    continue;
                }
                d.ValidateSlot(section.PresetSlot.Value);
                if (section.Scene is < 1 or > 8 ||
                    !(section.Scene != null && section.DefaultScene == null || section.Scene == null && section.DefaultScene == true))
                    throw new InvalidDataException("Enabled sections require exactly one scene (1–8) or default_scene=true.");
                string selection = section.DefaultScene == true ? "default" : "explicit";
                if (!saved.TryGetValue(section.PresetSlot.Value, out var capture))
                {
                    unresolved.Add(new("preset-not-in-inventory", song.Number, section.Number, null, null, section.PresetSlot));
                    sections.Add(new(section.Number, section.Name, section.PresetSlot, selection, section.Scene, "unresolved", null, null, null, null));
                    continue;
                }
                int defaultScene = BinaryPrimitives.ReadUInt16LittleEndian(capture.Image.Body) + 1;
                if (defaultScene is < 1 or > 8 || capture.Image.SceneNames.Length != 8)
                    throw new InvalidDataException("Referenced capture has an invalid saved default scene or scene-name table.");
                string hash = PresetBody.Sha256(capture.Sysex);
                referenced[capture.Image.Slot] = new(capture.Image.Slot, hash, PresetBody.Sha256(capture.Image.RawImage),
                    PresetBody.Sha256(capture.Image.Body), defaultScene);
                int scene = section.Scene ?? defaultScene;
                sections.Add(new(section.Number, section.Name, section.PresetSlot, selection, section.Scene, "resolved-from-retained-capture",
                    scene, capture.Image.Name, capture.Image.SceneNames[scene - 1], hash));
            }
            songs.Add(new(song.Number, song.Name, sections.ToArray()));
        }
        List<SetlistPreview> sets = [];
        foreach (var set in request.Setlists.OrderBy(s => s.Number))
        {
            Name(set.Name);
            if (set.Songs == null || set.Songs.Length > 32 || set.Songs.Any(s => s is < 1 or > 128))
                throw new InvalidDataException("A setlist is a compact ordered sequence of up to 32 song numbers (1–128).");
            List<SetlistPositionPreview> positions = [];
            for (int i = 0; i < set.Songs.Length; i++)
            {
                bool found = songMap.TryGetValue(set.Songs[i], out var song);
                if (!found) unresolved.Add(new("song-definition-missing", set.Songs[i], null, set.Number, i + 1, null));
                positions.Add(new(i + 1, set.Songs[i], song?.Name, found ? "defined" : "unresolved"));
            }
            sets.Add(new(set.Number, set.Name, set.Number == request.ActiveSetlist, positions.ToArray()));
        }
        if (request.ActiveSetlist is int active && !request.Setlists.Any(s => s.Number == active))
            unresolved.Add(new("active-setlist-definition-missing", null, null, active, null, null));

        return new
        {
            schema_version = 1, protocol_release = ProtocolRelease.Version, format = "toolkit-setlists-songs-proposal",
            request_sha256 = PresetBody.Sha256(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)), request,
            manufacturer_profile = new { source_id = SourceId, document_version = "1.04", document_date = "2023-10-30",
                minimum_firmware = minimum, fc_controller_required = d.Model == "Axe-Fx III" },
            inventory = new { model = d.Model, capacity = d.PresetCapacity, firmware = context.Firmware,
                capacity_evidence = context.CapacityEvidence, capture_complete = inventoryCompleted,
                requested_count = requestedSlots.Length, retained_count = saved.Count, covers_full_capacity = saved.Count == d.PresetCapacity,
                referenced_presets = referenced.Values.ToArray(), freshness = "retained-offline-evidence-only" },
            songs, setlists = sets, active_setlist = request.ActiveSetlist, unresolved_dependencies = unresolved,
            dependencies_resolved = unresolved.Count == 0, device_executable = false, editor_importable = false, ports_opened = false,
            limits = new[] { "Proposed records only: this does not inspect, preserve, replace or merge existing system data.",
                "Omitted sections are disabled in this proposal; omitted songs/setlists do not authorize deletion or preservation on a device.",
                "DEFAULT remains a symbolic selection; its resolved scene preview applies only to the retained saved capture.",
                "Preset slot references do not follow preset organization automatically; replan against the resulting inventory.",
                "FC presence is caller-declared; no footswitch layout, MIDI playback, system backup/restore or hardware qualification." }
        };
    }

    public static object Write(object report, string destination)
    {
        string root = Path.GetFullPath(destination);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Plan destination already exists; choose a new directory.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        Directory.CreateDirectory(root);
        using (var file = new FileStream(Path.Combine(root, "plan.json"), FileMode.CreateNew)) file.Write(bytes);
        return new { plan_sha256 = PresetBody.Sha256(bytes), plan = report, ports_opened = false };
    }

    private static void Name(string value)
    {
        // Portable toolkit policy; the manufacturer specifies length, not this character repertoire.
        if (value == null || value.Length > 10 || value.Any(c => c is < ' ' or > '~') || value.EndsWith(' '))
            throw new InvalidDataException("Toolkit names use 0–10 printable ASCII characters without trailing spaces.");
    }
    private static void DistinctNumbers(IEnumerable<int?> values, int maximum, string label)
    {
        int?[] array = values.ToArray();
        if (array.Any(n => n == null || n < 1 || n > maximum) || array.Distinct().Count() != array.Length)
            throw new InvalidDataException($"Distinct {label} numbers must be 1–{maximum}.");
    }
    private static void NoDuplicateMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate request property."); NoDuplicateMembers(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) NoDuplicateMembers(item);
    }
}
