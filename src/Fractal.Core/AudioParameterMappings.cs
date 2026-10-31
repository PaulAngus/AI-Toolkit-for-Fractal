using System.Text.Json;

namespace Fractal.Core;

public sealed record EditorParameterMapping(string Family, string EditorFamily, string Symbol, int ParameterId,
    string? DisplayLabel, string UnitHint, double? DisplayMinimumHint, double? DisplayMaximumHint,
    string CalibrationEvidence, bool Addressable);
public sealed record EditorParameterCatalogue(string Model, int Revision, string CatalogueSha256, string SourceRevision,
    string SourceId, string SourceUrl, string SourceSha256, string AddressSpace, string Qualification,
    bool HardwareQualified, bool WriteSupported, EditorParameterMapping[] Entries);

/// <summary>Independent model editor IDs, not saved offsets or executable commands.
/// Hints are documentary evidence and must never be used as automatic value conversions.</summary>
internal static class AudioParameterMappings
{
    private static readonly byte[] Bytes = ReadBytes();
    private static readonly JsonElement Catalogue = Parse();
    private static byte[] ReadBytes()
    {
        using var stream = typeof(AudioParameterMappings).Assembly.GetManifestResourceStream("Fractal.Core.AudioParameterMappings.json")
            ?? throw new InvalidDataException("Contained audio parameter mappings are missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
    private static JsonElement Parse()
    {
        using var doc = JsonDocument.Parse(Bytes);
        var root = doc.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 || root.GetProperty("revision").GetInt32() != 1)
            throw new InvalidDataException("Unsupported audio parameter catalogue version.");
        return root.Clone();
    }
    public static EditorParameterCatalogue ForModel(string model, string? family)
    {
        var data = Catalogue.GetProperty("models").EnumerateArray().Single(p => p.GetProperty("model").GetString() == model);
        var entries = data.GetProperty("entries").Deserialize<EditorParameterMapping[]>(SnapshotExport.JsonOptions)
            ?? throw new InvalidDataException("Missing editor parameters.");
        // Symbols are identities; legacy aliases may deliberately share a numeric ID.
        if (entries.Any(e => e.ParameterId < 0 || e.Addressable != (e.ParameterId <= 16383)) ||
            entries.Select(e => (e.Family, e.Symbol)).Distinct().Count() != entries.Length)
            throw new InvalidDataException("Invalid editor parameter mappings.");
        return new(model, 1, PresetBody.Sha256(Bytes), Catalogue.GetProperty("source_revision").GetString()!,
            data.GetProperty("source_id").GetString()!, data.GetProperty("source_url").GetString()!, data.GetProperty("source_sha256").GetString()!,
            "editor-parameter-id-not-saved-offset", Catalogue.GetProperty("scope").GetString()!, false, false,
            entries.Where(e => family == null || family.Equals(e.Family, StringComparison.OrdinalIgnoreCase)).ToArray());
    }
}
