using System.Text.Json;
namespace Fractal.Core;

public static class SnapshotExport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
    public static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);
    public static SavedCapture DecodeFile(DeviceDefinition d, byte[] sysex)
    {
        PresetFileInspection file = PresetFile.Inspect(d, sysex);
        int slot = file.HeaderPayload[0] << 7 | file.HeaderPayload[1];
        if (slot >= d.PresetCapacity) throw new InvalidDataException("File does not contain an addressed saved-preset capture; use check-backup to inspect opaque backup headers.");
        var image = new StoredPresetImage(slot, file.Name, file.SceneNames, file.RawImage, file.Body);
        return new(image, sysex.ToArray());
    }
    public static ExportResult Write(string destination, DeviceContext context, SavedCapture[] captures,
        CurrentState? before = null, CurrentState? after = null)
    {
        if (captures.Length == 0 || captures.Select(c => c.Image.Slot).Distinct().Count() != captures.Length)
            throw new ArgumentException("Export requires distinct captured preset slots.");
        List<PresetSnapshot> presets = [];
        List<DecodeError> decodeErrors = [];
        foreach (var capture in captures.OrderBy(c => c.Image.Slot))
        {
            // Recheck the public export boundary; callers cannot label arbitrary bytes as verified.
            var checkedCapture = DecodeFile(context.Definition, capture.Sysex);
            if (checkedCapture.Image.Slot != capture.Image.Slot || !checkedCapture.Image.RawImage.SequenceEqual(capture.Image.RawImage) ||
                !checkedCapture.Image.Body.SequenceEqual(capture.Image.Body)) throw new InvalidDataException("Capture and decoded image disagree.");
            try { presets.Add(PresetBody.Decode(context.Definition, checkedCapture.Image)); }
            catch (InvalidDataException error) { decodeErrors.Add(new(capture.Image.Slot, error.Message)); }
        }
        string target = Path.GetFullPath(destination);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Export destination already exists; choose a new directory.");
        Directory.CreateDirectory(target);
        object[] files = captures.OrderBy(c => c.Image.Slot).Select(c =>
        {
            string filename = $"preset-{c.Image.Slot:D4}.syx";
            using (var file = new FileStream(Path.Combine(target, filename), FileMode.CreateNew)) file.Write(c.Sysex);
            return (object)new { slot = c.Image.Slot, file = filename, sysex_sha256 = PresetBody.Sha256(c.Sysex),
                raw_sha256 = PresetBody.Sha256(c.Image.RawImage), body_sha256 = PresetBody.Sha256(c.Image.Body),
                integrity = new[] { "frame-xor", "footer-word-xor", "raw-crc16-aa55", "bounded-huffman" } };
        }).ToArray();
        var d = context.Definition;
        var snapshot = new
        {
            schema_version = 3,
            device = new { model = d.Model, firmware = context.Firmware, preset_capacity = d.PresetCapacity,
                capacity_evidence = context.CapacityEvidence,
                capabilities = new { scene_midi = true, scene_count = 8, message_slots = 8 },
                scene_midi_profile = new { layout = d.SceneMidi,
                    validation = d.Model == "FM9" && context.Firmware == "12.00" ?
                        "FM9-12.00 saved/live and PC/CC operation evidence; exact hardware scope is recorded separately" :
                        "provisional saved/live profile; no toolkit physical qualification for this model/firmware" },
                block_catalogue = BlockCatalogue.Scope(d.Model, context.Firmware),
                protocol_release = ProtocolRelease.Version, validation = "implementation-evidence; model/firmware physical validation is recorded separately" },
            source = new { kind = before == null ? "offline-saved-capture" : "live-saved-read", state_before = before,
                state_after = after, state_preserved = before == null ? (bool?)null : before == after,
                consistency = "independent-saved-reads; no device-wide atomic snapshot", captures = files },
            presets, decode_errors = decodeErrors
        };
        byte[] snapshotBytes = System.Text.Encoding.UTF8.GetBytes(Json(snapshot) + "\n");
        using (var file = new FileStream(Path.Combine(target, "snapshot.json"), FileMode.CreateNew)) file.Write(snapshotBytes);
        // Manifest is written last; its presence marks a completed export. Never overwrite artifacts.
        var manifest = new { schema_version = 1, completed_utc = DateTimeOffset.UtcNow, protocol_release = ProtocolRelease.Version,
            snapshot_file = "snapshot.json", snapshot_sha256 = PresetBody.Sha256(snapshotBytes), captures = files,
            decoded_presets = presets.Count, decode_errors = decodeErrors, restore_verified = false,
            note = "Saved-read captures are retained evidence, not verified device restore streams." };
        using var manifestFile = new FileStream(Path.Combine(target, "manifest.json"), FileMode.CreateNew);
        using var writer = new StreamWriter(manifestFile); writer.WriteLine(Json(manifest));
        return new(presets.Count, decodeErrors.ToArray());
    }
}
public sealed record DecodeError(int Slot, string Reason);
public sealed record ExportResult(int DecodedPresets, DecodeError[] DecodeErrors);
