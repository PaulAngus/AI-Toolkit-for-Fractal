using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fractal.Core;

public sealed record EditReadScope(string ProfileId, int ProfileRevision, string CatalogueSha256,
    string Model, int Capacity, string Firmware, string FirmwareOrigin, string FirmwareQualification,
    string FirmwareScope, bool HistoricalLayoutEvidence, bool HardwareQualified, JsonElement Sources);
public sealed record EditReadCapability(string Operation, string ReadSupport, bool WriteSupported, string Reason);
public sealed record EditCapabilityReport(int SchemaVersion, string ProtocolRelease, EditReadScope Scope,
    EditReadCapability[] Capabilities, bool PortsOpened, bool ExecutionSupported);
public sealed record SavedParameterDescriptor(string Key, string Family, string Scope, string ValueType,
    string Unit, int Minimum, int Maximum, int Resolution, string SavedEncoding, string OffsetOrigin, int[] SavedOffsets,
    int Columns, int Rows, string[] Channels, int[] EffectIds, string ReadSupport, bool WriteSupported,
    string EnumResolution, string ResetBehavior, string ModifierSupport,
    string Scale = "identity", SavedFieldCondition? Condition = null, string RangeDomain = "raw-u16");
public sealed record SavedFieldCondition(string Key, int EqualsRaw);
public sealed record EditParameterReport(int SchemaVersion, EditReadScope Scope, string? Family,
    SavedParameterDescriptor[] Parameters, string[] Limitations, bool PortsOpened, EditorParameterCatalogue EditorParameters, BuiltinScaleReport? BuiltinScales = null);

/// <summary>Contained, model-specific saved-read candidates. No write or live protocol factories.</summary>
public static class EditReadCatalogue
{
    public const string Reference = "knowledge-base/catalog/edit-read-profiles.json";
    private static readonly byte[] Bytes = ReadBytes();
    private static readonly JsonElement Catalogue = Parse();
    private static byte[] ReadBytes()
    {
        using var stream = typeof(EditReadCatalogue).Assembly.GetManifestResourceStream("Fractal.Core.EditReadProfiles.json")
            ?? throw new InvalidDataException("Contained edit read profiles are missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
    private static JsonElement Parse()
    {
        using var doc = JsonDocument.Parse(Bytes);
        var root = doc.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 || root.GetProperty("revision").GetInt32() != 3)
            throw new InvalidDataException("Unsupported edit read catalogue version.");
        return root.Clone();
    }
    internal static SavedReadProfile Select(DeviceDefinition device, string firmware)
    {
        if (device != DeviceDefinition.Create(device.Model, device.PresetCapacity))
            throw new ArgumentException("Use an established device definition.");
        if (firmware == null || !Regex.IsMatch(firmware, @"\A[0-9]{1,3}\.[0-9]{2}\z"))
            throw new ArgumentException("Firmware must be caller-recorded major.minor (e.g. 12.00), never inferred from a capture.");
        var candidates = Catalogue.GetProperty("models").EnumerateArray()
            .Where(p => p.GetProperty("model").GetString() == device.Model).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("No unique model-specific saved read profile.");
        var data = candidates[0];
        bool historical = data.GetProperty("historical_layout_firmware").EnumerateArray().Any(f => f.GetString() == firmware);
        var scope = new EditReadScope(data.GetProperty("profile_id").GetString()!, 3, PresetBody.Sha256(Bytes),
            device.Model, device.PresetCapacity, firmware, "caller-supplied-not-inferred",
            "provisional-read-profile", data.GetProperty("firmware_scope").GetString()!, historical, false,
            Catalogue.GetProperty("sources"));
        return new(device, data, scope);
    }
    public static EditCapabilityReport Capabilities(DeviceDefinition device, string firmware)
    {
        var p = Select(device, firmware);
        return new(1, ProtocolRelease.Version, p.Scope,
        [
            new("saved-scene-names", "candidate-offline-read", false, "32-byte fields; raw bytes retained; encoding checked per name."),
            new("saved-instance-records", "candidate-offline-read", false, "Requires a unique exact grid/prefix match; never shape-order inference."),
            new("amp.type", "candidate-offline-read", true, "Complete-image numeric selection with explicit preserve-other-fields policy; exact model record required."),
            new("amp.scene-state", "candidate-offline-read", true, "Absolute saved channel/bypass assignments; other controller/Scene Ignore bytes preserved."),
            new("audio-parameters", "candidate-offline-read", true, "Named saved fields are executable through plan-edit; units and quantization appear in review. Editor IDs alone are not saved offsets."),
            new("capture-calibrated-parameters", "exact-witness-read", true, "Exact caller-labelled single-word calibration witnesses and matching block context; no guessed interpolation or hardware qualification."),
            new("scene-ignore-authoring", "exact-witness-read", true, "Explicit block-scoped off/on calibration captures, with complete unrelated-byte preservation."),
            new("modifier-authoring", "source-qualified-chain-read", true, "Source edits, complete templates and named curve fields with matching built-in scales; exact binding ownership and preservation. Query builtin_scales for model/firmware coverage."),
            new("routing-graph", "candidate-offline-read", true, "Adjacent grid cables, I/O through paths, documented implicit Send/Return pairs, feedback components and explicit loop intent through complete-image authoring."),
            new("copy-block-settings", "candidate-offline-read", true, "Complete explicit same-model/firmware instance template, including single-channel Return settings; shared modifiers and grid preserved."),
            new("private-template-library", "integrity-checked-offline-library", true, "Named block/channel donor captures with exact entry hashes; ordinary copy/insertion plans, private metadata and caller-declared firmware provenance."),
            new("swap-channels", "candidate-offline-read", true, "Two complete rows from one immutable baseline, explicit preserve/follow scene-reference policy; shared modifier composition refused, no blind replay or sound-equivalence claim."),
            new("scene-midi-messages", "candidate-offline-read", true, "Combine template insertion and PC/CC configuration, explicit occupied-slot replacement, or exact expected-word selective clear; independent complete live-table verification."),
            new("live-block-control", "live-status-read", true, "Absolute scene/channel/bypass commands through capture-bound LiveControlScope and exact MCP review; no Save, no toggle/replay, complete preservation and effective status readback. Physical qualification remains separate."),
            new("scene-name-edit", "candidate-offline-read", true, "Plan and execute 31-byte printable ASCII names with explicit termination and complete preservation."),
            new("connected-insertion-removal", "candidate-offline-read", true, "Explicit same-model/firmware template initialization, chosen cells, exact edges and removal policy; complete-image verification.")
        ], false, true);
    }
    public static EditParameterReport Parameters(DeviceDefinition device, string firmware, string? family = null)
    {
        if (family != null && (string.IsNullOrWhiteSpace(family) || family.Length > 80))
            throw new ArgumentException("Family must be a nonempty name of at most 80 characters.");
        var p = Select(device, firmware);
        SavedParameterDescriptor[] values = new[] { p.AmpType }.Concat(p.AudioFields)
            .Where(v => family == null || family.Equals(v.Family, StringComparison.OrdinalIgnoreCase))
            .Select(v => v with { WriteSupported = true }).ToArray();
        return new(1, p.Scope, family, values,
            ["Saved descriptors are a bounded subset. Editor parameter IDs are not saved offsets; display hints are not calibrated conversions.",
             "plan-edit writes mapped saved fields through complete-image upload. Numeric selections explicitly preserve other fields; controllers remain preserved. Physical qualification and audible/effective behavior are separate."],
            false, AudioParameterMappings.ForModel(device.Model, family), Fractal.Core.BuiltinScales.Catalogue(device, firmware, family));
    }
}

internal sealed class SavedReadProfile
{
    public EditReadScope Scope { get; }
    public JsonElement Data { get; }
    private JsonElement Amp => Data.GetProperty("amp");
    public int PrefixDelta => Data.GetProperty("instance_prefix_delta").GetInt32();
    public int[] ChannelOffsets => Integers(Amp, "channel_offsets");
    public int[] BypassOffsets => Integers(Amp, "bypass_offsets");
    public int RoutingMask => Data.GetProperty("routing").GetProperty("known_mask").GetInt32();
    public string RoutingEncoding => Data.GetProperty("routing").GetProperty("encoding").GetString()!;
    public SavedParameterDescriptor[] AudioFields => Data.GetProperty("audio_fields").EnumerateArray().SelectMany(entry =>
        entry.GetProperty("columns").EnumerateArray().Select(shape =>
        {
            int columns = shape.GetInt32();
            var condition = entry.TryGetProperty("condition", out var c)
                ? new SavedFieldCondition(c.GetProperty("key").GetString()!, c.GetProperty("equals").GetInt32()) : null;
            string scale = entry.GetProperty("scale").GetString()!;
            return new SavedParameterDescriptor(entry.GetProperty("key").GetString()!, entry.GetProperty("family").GetString()!,
                "channel", scale == "identity" ? "numeric-identifier" : "number", entry.GetProperty("unit").GetString()!,
                0, 65535, 1, "uint16-little-endian", "record-shape-offset",
                Integers(entry.GetProperty("offsets_by_columns"), columns.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                columns, entry.GetProperty("rows").GetInt32(), ["A", "B", "C", "D"], Integers(entry, "effect_ids"),
                entry.GetProperty("read_support").GetString()!, entry.GetProperty("write_supported").GetBoolean(),
                "unknown-numeric-ids-retained", "unknown", "unsupported", scale, condition);
        })).ToArray();
    public SavedParameterDescriptor AmpType => new("amp.type", "Amp", "channel", "numeric-identifier", "model-id",
        0, 65535, 1, "uint16-little-endian", "record-shape-offset", Integers(Amp, "type_offsets"), Amp.GetProperty("columns").GetInt32(),
        Amp.GetProperty("rows").GetInt32(), ["A", "B", "C", "D"], Integers(Amp, "effect_ids"),
        "candidate-offline-read", false, "unknown-numeric-ids-retained", "unknown", "unsupported");
    public SavedReadProfile(DeviceDefinition device, JsonElement data, EditReadScope scope)
    {
        Data = data; Scope = scope;
        var a = AmpType;
        var n = Data.GetProperty("scene_names");
        var r = Data.GetProperty("routing");
        if (PrefixDelta != -12 || a.Columns != device.AmpColumns || a.Rows != 4 || a.EffectIds.Length != device.MaxAmps ||
            a.SavedOffsets.Length != 4 || ChannelOffsets.Length != 8 || BypassOffsets.Length != 8 ||
            a.SavedOffsets.Concat(ChannelOffsets).Concat(BypassOffsets).Any(o => o % 2 != 0 || o < PrefixDelta || o + 2 > PrefixDelta + (23 + a.Columns * a.Rows) * 2) ||
            n.GetProperty("offset").GetInt32() != 4 || n.GetProperty("count").GetInt32() != 8 || n.GetProperty("bytes_per_name").GetInt32() != 32 ||
            n.GetProperty("encoding").GetString() != "ascii" || n.GetProperty("write_supported").GetBoolean() ||
            RoutingEncoding != "previous-column-source-row-mask" || RoutingMask != (1 << device.GridRows) - 1 ||
            !r.GetProperty("edge_decode_supported").GetBoolean() || r.GetProperty("write_supported").GetBoolean())
            throw new InvalidDataException("Unsupported or inconsistent saved read profile.");
        var fields = AudioFields;
        foreach (var field in fields)
        {
            if (field.Columns <= 0 || field.Rows != 4 || field.SavedOffsets.Length != 4 || field.WriteSupported ||
                field.Scale is not ("identity" or "u16-to-0-10") || field.EffectIds.Length == 0 ||
                field.SavedOffsets.Any(o => o % 2 != 0 || o < PrefixDelta || o + 2 > PrefixDelta + (23 + field.Columns * field.Rows) * 2) ||
                field.EffectIds.Any(id => BlockCatalogue.Resolve(device.Model, id).Family != field.Family) ||
                fields.Count(f => f.Key == field.Key && f.Columns == field.Columns) != 1)
                throw new InvalidDataException("Unsupported or inconsistent saved audio field.");
            if (field.Condition is { } condition && !fields.Any(f => f.Key == condition.Key && f.Columns == field.Columns &&
                f.Rows == field.Rows && f.Condition == null && field.EffectIds.All(f.EffectIds.Contains)))
                throw new InvalidDataException("Saved audio condition has no matching discriminator.");
        }
    }
    private static int[] Integers(JsonElement item, string key) => item.GetProperty(key).EnumerateArray().Select(n => n.GetInt32()).ToArray();
}
