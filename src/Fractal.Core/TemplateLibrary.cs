using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fractal.Core;

public sealed record TemplateImportRequest(int SchemaVersion, string Model, int Capacity, string Firmware,
    int EffectId, string Kind, string Name, string[] Tags, string Provenance, string CaptureSha256, string? Channel = null);
public sealed record TemplateEntry(int SchemaVersion, TemplateImportRequest Metadata, int SourceSlot,
    int Columns, int Rows, string SettingsSha256, string Capture);
public sealed record TemplateApplyRequest(int SchemaVersion, string Model, int Capacity, string Firmware,
    int Slot, string Persistence, string BaseSha256, int EffectId, string Kind, string? Channel = null,
    int? Row = null, int? Column = null, string? Policy = null);

/// <summary>Private immutable entries containing complete donor captures. All operations are offline.
/// Hashes bind bytes and declarations, not hardware origin or authorization.</summary>
public static class TemplateLibrary
{
    private const int MaximumEntryBytes = 200_000;
    private static readonly Regex Identifier = new("^[a-f0-9]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hash = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);

    public static T Parse<T>(byte[] json)
    {
        if (json.Length > MaximumEntryBytes) throw new InvalidDataException("Template JSON exceeds its size bound.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            void Check(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var p in e.EnumerateObject())
                    { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate template JSON field."); Check(p.Value); }
                }
                else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Check(v);
            }
            Check(doc.RootElement);
            return JsonSerializer.Deserialize<T>(json, PresetEditPlan.RequestJson) ?? throw new InvalidDataException("Missing template JSON.");
        }
        catch (JsonException e) { throw new InvalidDataException("Invalid template JSON.", e); }
    }

    public static TemplateEntry Create(TemplateImportRequest input, byte[] capture)
    {
        capture = capture.ToArray();
        // Freeze caller-owned tags before validating/storing.
        var m = Parse<TemplateImportRequest>(JsonSerializer.SerializeToUtf8Bytes(input, PresetEditPlan.RequestJson));
        var d = DeviceDefinition.Create(m.Model, m.Capacity);
        _ = EditReadCatalogue.Capabilities(d, m.Firmware);
        bool Text(string? v, int max) => v != null && v.Length is > 0 && v.Length <= max && v == v.Trim() && !v.Any(char.IsControl);
        if (m.SchemaVersion != 1 || m.Model != d.Model || m.Capacity != d.PresetCapacity ||
            m.Kind is not ("block" or "channel") || !Text(m.Name, 80) || !Text(m.Provenance, 512) ||
            m.Tags == null || m.Tags.Length > 16 || m.Tags.Any(t => !Text(t, 32)) ||
            m.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Tags.Length ||
            m.CaptureSha256 == null || !Hash.IsMatch(m.CaptureSha256) || capture.Length > 60000 ||
            PresetBody.Sha256(capture) != m.CaptureSha256 ||
            (m.Kind == "channel" ? m.Channel is not ("A" or "B" or "C" or "D") : m.Channel != null))
            throw new ArgumentException("Invalid template metadata, channel, tags or capture hash.");
        m = m with { Tags = m.Tags.Order(StringComparer.OrdinalIgnoreCase).ToArray() };
        var read = PresetEditProjection.Decode(d, m.Firmware, capture);
        var block = read.Blocks.SingleOrDefault(b => b.EffectId == m.EffectId && b.Record != null && b.Cells.Length == 1);
        var record = block?.Record ?? throw new InvalidDataException("Template needs a uniquely placed exact instance record.");
        if (record.PrefixOffset < 0x104 + d.GridRows * d.GridColumns * 4 || m.Kind == "channel" && record.Rows != 4)
            throw new InvalidDataException("Template requires a supported complete instance/channel interval.");
        byte[] body = Convert.FromHexString(read.BodyHex);
        int start = m.Kind == "block" ? record.PrefixOffset : record.ShapeOffset + 34 + (m.Channel![0] - 'A') * record.Columns * 2;
        int length = m.Kind == "block" ? record.ByteLength : record.Columns * 2;
        return new(1, m, read.Slot, record.Columns, record.Rows,
            PresetBody.Sha256(body.AsSpan(start, length).ToArray()), Convert.ToBase64String(capture));
    }

    public static TemplateEntry ReadEntry(string path, string? expectedSha256)
    {
        SafePath(path, false);
        if (new FileInfo(path).Length > MaximumEntryBytes) throw new InvalidDataException("Template entry exceeds size bound.");
        byte[] bytes = File.ReadAllBytes(path);
        if (expectedSha256 != null && (!Hash.IsMatch(expectedSha256) || PresetBody.Sha256(bytes) != expectedSha256))
            throw new InvalidDataException("Template entry hash changed.");
        var entry = Parse<TemplateEntry>(bytes);
        if (entry.SchemaVersion != 1 || entry.Metadata == null || entry.Capture == null)
            throw new InvalidDataException("Unsupported template entry.");
        TemplateEntry resolved;
        try { resolved = Create(entry.Metadata, Convert.FromBase64String(entry.Capture)); }
        catch (FormatException e) { throw new InvalidDataException("Invalid template capture.", e); }
        if (entry.SourceSlot != resolved.SourceSlot || entry.Columns != resolved.Columns || entry.Rows != resolved.Rows ||
            entry.SettingsSha256 != resolved.SettingsSha256)
            throw new InvalidDataException("Template shape, source or settings hash differs from its complete capture.");
        return resolved;
    }

    public static object Summary(TemplateEntry entry) => new { schema_version = entry.SchemaVersion,
        entry.Metadata.Name, entry.Metadata.Tags, entry.Metadata.Kind, entry.Metadata.Model,
        entry.Metadata.Capacity, entry.Metadata.Firmware, entry.Metadata.EffectId, entry.Metadata.Channel,
        entry.Metadata.Provenance, entry.Metadata.CaptureSha256, entry.SourceSlot, entry.Columns, entry.Rows, entry.SettingsSha256,
        footprint = entry.Metadata.Kind == "channel" ? "complete-channel-row; scene-assignments-and-shared-modifiers-preserved"
            : "complete-instance-including-scene-assignments; grid-and-shared-modifiers-preserved",
        qualification = "Caller-declared firmware/provenance; complete capture integrity and shape checked; no factory-default or hardware-origin claim.",
        hardware_qualified = false, ports_opened = false };

    public static object Import(string library, TemplateImportRequest request, byte[] capture)
    {
        var entry = Create(request, capture);
        SafePath(library, true); Directory.CreateDirectory(library);
        if (Directory.EnumerateFiles(library, "*.json").Take(5001).Count() >= 5000) throw new IOException("Library entry limit reached.");
        string id = Guid.NewGuid().ToString("N"), path = EntryPath(library, id);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry, PresetEditPlan.RequestJson);
        string pending = Path.Combine(library, id + ".pending");
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(pending, path, false);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
        return new { entry_id = id, entry_sha256 = PresetBody.Sha256(bytes), entry = Summary(entry), ports_opened = false };
    }

    public static string EntryPath(string library, string id)
    {
        if (!Identifier.IsMatch(id)) throw new ArgumentException("Use an opaque 32-character template entry ID.");
        SafePath(library, true);
        string path = Path.Combine(Path.GetFullPath(library), id + ".json"); SafePath(path, true); return path;
    }

    public static object Inspect(string library, string id, string expectedSha256)
    {
        var entry = ReadEntry(EntryPath(library, id), expectedSha256);
        return new { entry_id = id, entry_sha256 = expectedSha256, entry = Summary(entry), ports_opened = false };
    }

    public static object List(string library, string? query = null, string? tag = null, int offset = 0, int limit = 20)
    {
        SafePath(library, false);
        if (offset < 0 || offset > 5000 || limit is < 1 or > 100 || query?.Length > 80 || tag?.Length > 32)
            throw new ArgumentException("Invalid template search or paging bounds.");
        var rows = new List<object>(); int matched = 0, scanned = 0;
        foreach (string path in Directory.EnumerateFiles(library, "*.json").Take(5001).Order(StringComparer.Ordinal))
        {
            if (++scanned > 5000) throw new InvalidDataException("Library entry limit exceeded.");
            string id = Path.GetFileNameWithoutExtension(path);
            if (!Identifier.IsMatch(id)) throw new InvalidDataException("Unknown file in template library.");
            // Read one byte snapshot so the returned hash always identifies the inspected content.
            SafePath(path, false);
            if (new FileInfo(path).Length > MaximumEntryBytes) throw new InvalidDataException("Template entry exceeds size bound.");
            byte[] bytes = File.ReadAllBytes(path); string hash = PresetBody.Sha256(bytes);
            var entry = ReadEntry(path, hash);
            if (query != null && !entry.Metadata.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !entry.Metadata.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))) continue;
            if (tag != null && !entry.Metadata.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;
            if (matched >= offset && rows.Count < limit) rows.Add(new { entry_id = id, entry_sha256 = hash, entry = Summary(entry) });
            matched++;
        }
        return new { entries = rows, total = matched, next_offset = offset + rows.Count < matched ? (int?)(offset + rows.Count) : null, ports_opened = false };
    }

    public static PresetEditPlan Plan(string entryPath, string expectedEntryHash, TemplateApplyRequest request, byte[] baseline)
    {
        if (expectedEntryHash == null || !Hash.IsMatch(expectedEntryHash)) throw new ArgumentException("Supply exact entry_sha256.");
        var entry = ReadEntry(entryPath, expectedEntryHash); var m = entry.Metadata;
        if (request.SchemaVersion != 1 || request.Model != m.Model || request.Capacity != m.Capacity ||
            request.Firmware != m.Firmware || request.EffectId != m.EffectId)
            throw new ArgumentException("Template context or exact instance differs from the target request.");
        PresetEditOperation operation;
        if (request.Kind == "copy-channel" && m.Kind == "channel" && request.Channel is "A" or "B" or "C" or "D" &&
            request.Row == null && request.Column == null && request.Policy == null)
            operation = new(request.Kind, EffectId: m.EffectId, Channel: request.Channel, TemplateChannel: m.Channel);
        else if (request.Kind == "copy-block-settings" && m.Kind == "block" && request.Channel == null &&
            request.Row == null && request.Column == null && request.Policy == null)
            operation = new(request.Kind, EffectId: m.EffectId);
        else if (request.Kind == "insert-block" && m.Kind == "block" && request.Channel == null &&
            request.Row != null && request.Column != null && request.Policy is null or "replace-shunt")
            operation = new(request.Kind, EffectId: m.EffectId, Row: request.Row, Column: request.Column, Policy: request.Policy);
        else throw new ArgumentException("Template kind and apply fields do not match a supported operation.");
        operation = operation with { TemplateCapture = entry.Capture, TemplateSha256 = m.CaptureSha256, TemplateFirmware = m.Firmware };
        return PresetEditPlan.Create(new(1, request.Model, request.Capacity, request.Firmware, request.Slot,
            request.Persistence, request.BaseSha256, [operation]), baseline);
    }

    private static void SafePath(string input, bool missingAllowed)
    {
        string full = Path.GetFullPath(input);
        for (string? path = full; path != null; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked template library paths are refused.");
        if (!missingAllowed && !File.Exists(full) && !Directory.Exists(full)) throw new IOException("Template path is missing.");
    }
}
