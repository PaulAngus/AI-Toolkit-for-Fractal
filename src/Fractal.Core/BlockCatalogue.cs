using System.Text.Json;
using System.Text.RegularExpressions;
namespace Fractal.Core;

public sealed record BlockAvailability(bool? DocumentedAvailable, int? DocumentedInstances,
    string SourceId, string Revision, string FirmwareQualification);
public sealed record BlockIdentity(int EffectId, string? Name, string? Family, int? Instance,
    string IdentificationStatus, string? CandidateName, string[] IdentitySourceIds, BlockAvailability Availability);

// One contained dataset and resolver shared by the exporter, CLI and AI adapters.
// Manufacturer-only Axe-Fx III mappings do not become FM9/FM3 identities by assumption.
public static class BlockCatalogue
{
    public const string Reference = "knowledge-base/catalog/block-identities.json";
    private static readonly string[] Models = ["FM9", "FM3", "Axe-Fx III"];
    private static readonly JsonElement Catalogue = Read();
    private static JsonElement Read()
    {
        using var stream = typeof(BlockCatalogue).Assembly.GetManifestResourceStream("Fractal.Core.BlockIdentities.json")
            ?? throw new InvalidDataException("Contained block catalogue is missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
    private static void Model(string model)
    {
        if (!Models.Contains(model)) throw new ArgumentException("Choose FM9, FM3 or Axe-Fx III.");
    }
    public static string? GridName(string model, int effectId)
    {
        Model(model);
        if (effectId == 0) return null;
        if (effectId is >= 1024 and <= 1029) return "Shunt";
        return Resolve(model, effectId).Name;
    }
    public static BlockIdentity Resolve(string model, int effectId)
    {
        Model(model);
        if (effectId is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(effectId));
        foreach (var entry in Catalogue.GetProperty("entries").EnumerateArray())
        {
            if (entry.GetProperty("effect_id").GetInt32() != effectId) continue;
            bool qualified = entry.GetProperty("identity_models").EnumerateArray().Any(m => m.GetString() == model);
            string label = entry.GetProperty("name").GetString()!;
            return new(effectId, qualified ? label : null, qualified ? entry.GetProperty("family").GetString() : null,
                qualified ? entry.GetProperty("instance").GetInt32() : null,
                qualified ? "source-mapped" : "unqualified-for-model", qualified ? null : label,
                entry.GetProperty("identity_source_ids").EnumerateArray().Select(s => s.GetString()!).ToArray(),
                new(entry.GetProperty("documented_available").GetProperty(model).GetBoolean(),
                    entry.GetProperty("documented_instances").GetProperty(model).GetInt32(),
                    "fractal-blocks-guide", "June 2023", "not-established-for-exact-firmware"));
        }
        return new(effectId, null, null, null, effectId == 0 ? "empty" : effectId is >= 1024 and <= 1029 ? "shunt" : "unknown",
            null, [], new(null, null, "fractal-blocks-guide", "June 2023", "not-established-for-exact-firmware"));
    }
    public static object Scope(string model, string? firmware)
    {
        Model(model);
        if (firmware != null && !Regex.IsMatch(firmware, @"^\d{1,3}\.\d{2}$"))
            throw new ArgumentException("Firmware must be a recorded major.minor version, e.g. 12.00.");
        return new { reference = Reference, catalogue_revision = Catalogue.GetProperty("catalogue_revision").GetInt32(),
            model, firmware, reference_date = Catalogue.GetProperty("reference_date").GetString(),
            sources = Catalogue.GetProperty("sources"), availability_revision = Catalogue.GetProperty("availability_revision").GetString(),
            firmware_scope = Catalogue.GetProperty("firmware_scope").GetString(), limits = Catalogue.GetProperty("limits"),
            hardware_qualified = false, parameter_decoding = false, insertion_authorized = false };
    }
    public static object Query(string model, string? firmware, string? query, int? effectId)
    {
        var scope = Scope(model, firmware);
        if (effectId is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(effectId));
        if (query != null && (query.Length > 200 || !Regex.IsMatch(query, "[A-Za-z0-9]")))
            throw new ArgumentException("Use a block name or abbreviation of at most 200 characters.");
        if (query == null && effectId == null) throw new ArgumentException("Provide a block name or effect ID.");
        string[] terms = Regex.Matches((query ?? "").ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).ToArray();
        bool Match(JsonElement entry)
        {
            string text = entry.GetProperty("name").GetString() + " " + entry.GetProperty("family").GetString() + " " +
                string.Join(" ", entry.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
            string[] words = Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).ToArray();
            return terms.All(term => words.Any(word => word.StartsWith(term, StringComparison.Ordinal)));
        }
        var entries = Catalogue.GetProperty("entries").EnumerateArray()
            .Where(e => (effectId == null || e.GetProperty("effect_id").GetInt32() == effectId) && Match(e)).ToArray();
        var matches = entries.Select(e => Resolve(model, e.GetProperty("effect_id").GetInt32())).ToList();
        if (effectId != null && query == null && matches.Count == 0) matches.Add(Resolve(model, effectId.Value));
        var unmapped = Catalogue.GetProperty("unmapped_families").EnumerateArray().Where(e => effectId == null &&
            terms.All(t => e.GetProperty("family").GetString()!.ToLowerInvariant().Contains(t, StringComparison.Ordinal))).ToArray();
        return new { schema_version = 1, scope, query, effect_id = effectId, matches, unmapped_families = unmapped, ports_opened = false };
    }
}
