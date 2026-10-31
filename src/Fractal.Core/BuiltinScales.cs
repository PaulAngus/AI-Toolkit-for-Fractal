using System.Buffers.Binary;
using System.Text.Json;

namespace Fractal.Core;

public sealed record SavedValueScale(double Minimum, double Maximum, string Unit, string Taper, int RawMaximum)
{
    public int Encode(double value)
    {
        if (!double.IsFinite(value) || value < Minimum || value > Maximum)
            throw new ArgumentException("Value is outside the declared physical range.");
        if (Taper == "int-raw")
        {
            if (value != Math.Truncate(value)) throw new ArgumentException("Discrete saved values must be whole numbers.");
            return (int)value;
        }
        double normalized = Taper == "linear" ? (value - Minimum) / (Maximum - Minimum) : Math.Log(value / Minimum) / Math.Log(Maximum / Minimum);
        return (int)Math.Round(normalized * RawMaximum, MidpointRounding.AwayFromZero);
    }
    public double? Decode(int raw) => raw < 0 || raw > RawMaximum || Taper == "int-raw" && (raw < Minimum || raw > Maximum) ? null : Taper == "int-raw" ? raw : Taper == "linear"
        ? Minimum + raw * (Maximum - Minimum) / RawMaximum : Minimum * Math.Pow(Maximum / Minimum, raw / (double)RawMaximum);
}
public sealed record BuiltinScaleField(string Key, string Family, int[] EffectIds, int Columns, int ParameterId,
    SavedValueScale Scale, string SourceId, string SourceLocator, string Qualification);
public sealed record BuiltinScaleProfile(string Model, string Firmware, BuiltinScaleField[] Fields, Dictionary<string, int> ModifierPercentFields,
    Dictionary<string, int> ModifierNormalizedFields, Dictionary<string, int> ModifierRawSelectors);
public sealed record BuiltinScaleReport(string Model, string Firmware, string CatalogueSha256, string Qualification,
    BuiltinScaleField[] Fields, Dictionary<string, int> ModifierPercentFields,
    Dictionary<string, int> ModifierNormalizedFields, Dictionary<string, int> ModifierRawSelectors, bool HardwareQualified = false);

/// <summary>Saved-word laws only. Live SET floats and editor display hints are different domains.</summary>
public static class BuiltinScales
{
    private static readonly byte[] Bytes = Load();
    private static readonly BuiltinScaleProfile[] Profiles = Parse();
    private static byte[] Load()
    {
        using var stream = typeof(BuiltinScales).Assembly.GetManifestResourceStream("Fractal.Core.SavedValueScales.json")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    private static BuiltinScaleProfile[] Parse()
    {
        using var doc = JsonDocument.Parse(Bytes);
        if (doc.RootElement.GetProperty("schema_version").GetInt32() != 1) throw new InvalidDataException("Unknown scale catalogue.");
        var profiles = doc.RootElement.GetProperty("model_profiles").Deserialize<BuiltinScaleProfile[]>(SnapshotExport.JsonOptions)!;
        if (profiles.Select(p => (p.Model, p.Firmware)).Distinct().Count() != profiles.Length) throw new InvalidDataException("Duplicate scale profiles.");
        foreach (var p in profiles)
        {
            _ = DeviceDefinition.Create(p.Model, 512);
            var modifierFields = p.ModifierPercentFields.Concat(p.ModifierNormalizedFields).Concat(p.ModifierRawSelectors).ToArray();
            if (modifierFields.Any(pair => pair.Value is < 1 or > 24 or 8 or 9) ||
                modifierFields.Select(pair => pair.Value).Distinct().Count() != modifierFields.Length ||
                modifierFields.Select(pair => pair.Key).Distinct().Count() != modifierFields.Length)
                throw new InvalidDataException("Modifier fields overlap bindings or one another.");
            if (p.Fields.Select(f => f.Key).Distinct().Count() != p.Fields.Length ||
                p.Fields.SelectMany(f => f.EffectIds.Select(id => (id, f.Columns, f.ParameterId))).Distinct().Count() != p.Fields.Sum(f => f.EffectIds.Length))
                throw new InvalidDataException("Duplicate saved scale identities or words.");
            foreach (var f in p.Fields)
                if (f.ParameterId < 0 || f.ParameterId >= f.Columns || f.EffectIds.Length == 0 ||
                    f.Scale.RawMaximum != 65534 || f.Scale.Taper is not ("linear" or "log" or "int-raw") ||
                    !double.IsFinite(f.Scale.Minimum) || !double.IsFinite(f.Scale.Maximum) || f.Scale.Maximum <= f.Scale.Minimum ||
                    f.Scale.Taper == "log" && f.Scale.Minimum <= 0 ||
                    f.Scale.Taper == "int-raw" && (f.Scale.Minimum < 0 || f.Scale.Maximum > f.Scale.RawMaximum ||
                        f.Scale.Minimum != Math.Truncate(f.Scale.Minimum) || f.Scale.Maximum != Math.Truncate(f.Scale.Maximum)) ||
                    f.EffectIds.Any(id => BlockCatalogue.Resolve(p.Model, id).Family != f.Family))
                    throw new InvalidDataException("Invalid saved scale descriptor: " + f.Key);
        }
        return profiles;
    }
    public static BuiltinScaleReport Catalogue(DeviceDefinition device, string firmware, string? family = null)
    {
        var profile = Profiles.SingleOrDefault(p => p.Model == device.Model && p.Firmware == firmware);
        return new(device.Model, firmware, PresetBody.Sha256(Bytes),
            "Exact source model/firmware scope; saved-word layout is source-derived, physical observations are reported upstream, not toolkit hardware qualification. Other supported operations remain enabled.",
            profile?.Fields.Where(f => family == null || f.Family.Equals(family, StringComparison.OrdinalIgnoreCase)).Select(f => f with { EffectIds = f.EffectIds.ToArray() }).ToArray() ?? [],
            profile == null ? [] : new Dictionary<string, int>(profile.ModifierPercentFields),
            profile == null ? [] : new Dictionary<string, int>(profile.ModifierNormalizedFields),
            profile == null ? [] : new Dictionary<string, int>(profile.ModifierRawSelectors));
    }
    public static BuiltinScaleField Field(DeviceDefinition device, string firmware, int effect, string key, int? columns = null) =>
        Catalogue(device, firmware).Fields.SingleOrDefault(f => f.Key == key && f.EffectIds.Contains(effect) && (columns == null || f.Columns == columns))
        ?? throw new InvalidDataException("No built-in saved scale matches this model, firmware, field and record shape.");
    public static object Apply(DeviceDefinition device, string firmware, byte[] body, SavedBlockRead block, PresetEditOperation operation)
    {
        var record = block.Record!;
        var field = Field(device, firmware, block.EffectId, operation.Key!, record.Columns);
        if (record.Rows != 4 || operation.Channel is not ("A" or "B" or "C" or "D") || operation.PhysicalValue == null || operation.Unit != field.Scale.Unit)
            throw new ArgumentException("A four-channel instance, explicit channel, physical_value and declared unit are required.");
        int offset = record.ShapeOffset + 34 + 2 * ((operation.Channel[0] - 'A') * record.Columns + field.ParameterId);
        int before = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset, 2)), raw = field.Scale.Encode(operation.PhysicalValue.Value);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)raw);
        return new { field.Key, operation.Channel, requested = operation.PhysicalValue, before = field.Scale.Decode(before),
            effective_saved_value = field.Scale.Decode(raw), raw_before = before, raw_after = raw, field.Scale, field.SourceId, field.SourceLocator, field.Qualification,
            catalogue_sha256 = Catalogue(device, firmware).CatalogueSha256,
            affected_scenes = block.Scenes.Where(s => s.Channel == operation.Channel).Select(s => s.Scene).ToArray(),
            effective_live_value = "type-tempo-controller-dependent-not-inferred" };
    }
}
