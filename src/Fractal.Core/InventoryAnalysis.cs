namespace Fractal.Core;

public static class InventoryAnalysis
{
    public static int[] ReadRoster(byte[] json, DeviceContext context)
    {
        if (json.Length > 4_000_000) throw new InvalidDataException("Roster exceeds bound.");
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json); var root = document.RootElement;
            if (root.GetProperty("model").GetString() != context.Definition.Model || root.GetProperty("firmware").GetString() != context.Firmware)
                throw new InvalidDataException("Roster model/firmware mismatch.");
            bool hasIds = root.TryGetProperty("ids", out var ids), hasEntries = root.TryGetProperty("entries", out var entries);
            if (root.TryGetProperty("range_complete", out var complete) && !complete.GetBoolean())
                throw new InvalidDataException("Live roster range is incomplete; retain it as evidence, not an availability catalogue.");
            if (hasIds == hasEntries) throw new InvalidDataException("Roster needs exactly one ids or entries array.");
            int[] values = hasIds ? ids.EnumerateArray().Select(v => v.GetInt32()).ToArray() :
                entries.EnumerateArray().Select(v => v.GetProperty("id").GetInt32()).ToArray();
            if (values.Length == 0 || values.Any(v => v is < 0 or > 65535) || values.Distinct().Count() != values.Length)
                throw new InvalidDataException("Roster must contain distinct uint16 identifiers.");
            return values;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed qualified roster JSON.", error); }
    }
    // Exclude wire address, CRC, compressed representation and preset name only. Every decoded body byte participates.
    private static string ContentKey(SavedCapture c) => PresetBody.Sha256(c.Image.RawImage.Take(4)
        .Concat(c.Image.RawImage.Skip(6).Take(2)).Concat(c.Image.RawImage.Skip(40).Take(32)).Concat(c.Image.Body).ToArray());
    public static object Compare(PresetInventory before, PresetInventory after)
    {
        var context = before.Context;
        if (context.Definition != after.Context.Definition || context.Firmware != after.Context.Firmware)
            throw new InvalidDataException("Inventory comparison requires matching model, capacity and firmware.");
        var old = before.ReadCaptures().ToDictionary(c => c.Image.Slot); var current = after.ReadCaptures().ToDictionary(c => c.Image.Slot);
        var slots = before.RequestedSlots.Union(after.RequestedSlots).Order().ToArray();
        var changes = slots.Select(slot =>
        {
            old.TryGetValue(slot, out var a); current.TryGetValue(slot, out var b);
            string status = a == null || b == null ? "unknown-missing-capture" : a.Image.RawImage.SequenceEqual(b.Image.RawImage) ? "identical-raw" :
                ContentKey(a) == ContentKey(b) ? a.Image.RawImage.AsSpan(8, 32).SequenceEqual(b.Image.RawImage.AsSpan(8, 32)) ? "representation-only" : "preset-name-change" : "content-changed";
            ScenePermutationResult? permutation = status == "content-changed" ?
                ScenePermutationAnalysis.Compare(context.Definition, context.Firmware, a!.Sysex, b!.Sysex) : null;
            return new { slot, status, before_name = a?.Image.Name, after_name = b?.Image.Name,
                before_raw_sha256 = a == null ? null : PresetBody.Sha256(a.Image.RawImage), after_raw_sha256 = b == null ? null : PresetBody.Sha256(b.Image.RawImage),
                changed_scene_names = a == null || b == null ? null : Enumerable.Range(1, 8).Where(s => a.Image.SceneNames[s - 1] != b.Image.SceneNames[s - 1]).ToArray(),
                scene_permutation = permutation };
        }).ToArray();
        var oldGroups = old.Values.GroupBy(ContentKey).ToDictionary(g => g.Key, g => g.Select(c => c.Image.Slot).Order().ToArray());
        var newGroups = current.Values.GroupBy(ContentKey).ToDictionary(g => g.Key, g => g.Select(c => c.Image.Slot).Order().ToArray());
        var matches = oldGroups.Keys.Intersect(newGroups.Keys).Order(StringComparer.Ordinal).Select(key => new {
            content_sha256 = key, before_slots = oldGroups[key], after_slots = newGroups[key],
            status = oldGroups[key].Length == 1 && newGroups[key].Length == 1 ? "unique-content-correspondence" : "ambiguous-identical-content",
            moved_candidate = oldGroups[key].Length == 1 && newGroups[key].Length == 1 && oldGroups[key][0] != newGroups[key][0] }).ToArray();
        bool full = before.Complete && after.Complete && old.Count == context.Definition.PresetCapacity && current.Count == context.Definition.PresetCapacity;
        return new { schema_version = 1, model = context.Definition.Model, firmware = context.Firmware, capacity = context.Definition.PresetCapacity,
            full_capacity_comparison = full, before_complete = before.Complete, after_complete = after.Complete,
            all_saved_raw_equal = full && changes.All(c => c.status == "identical-raw"), changes, content_matches = matches,
            physical_identity = "not-established", automatic_reassignment = false, ports_opened = false,
            fingerprint_scope = "entire decoded body plus opaque raw header excluding CRC, preset name and compression lengths; wire address/compression/padding excluded",
            scene_identity = "Exact known-field scene permutations require complete decoded-body byte equality after moving scene names, placed block channel/bypass words and Scene MIDI banks. Ambiguous or unexplained changes do not establish scene identity or sound equivalence." };
    }
    public static object AmpUsage(PresetInventory inventory, int? ampType = null, int[]? availableIds = null, int offset = 0, int? limit = null)
    {
        if (offset < 0 || limit < 1) throw new ArgumentException("Invalid amp usage page bounds.");
        if (ampType is < 0 or > 65535 || availableIds?.Any(id => id is < 0 or > 65535) == true)
            throw new ArgumentException("Amp identifiers must be uint16 values from a context-qualified roster.");
        var context = inventory.Context; var captures = inventory.ReadCaptures(); var errors = new List<object>();
        var uses = new List<object>(); var used = new HashSet<int>();
        foreach (var capture in captures)
        {
            var projection = PresetEditProjection.Decode(context.Definition, context.Firmware, capture.Sysex);
            if (projection.Routing.Edges == null) errors.Add(new { slot = capture.Image.Slot, reason = "unsupported-body-layout" });
            foreach (var block in projection.Blocks.Where(b => b.Identity.Family == "Amp"))
            {
                var types = block.Parameters.Where(p => p.Key == "amp.type").ToArray();
                if (types.Length != 4 || block.Scenes.Length != 8 || block.Scenes.Any(s => s.Status != "decoded-candidate"))
                    errors.Add(new { slot = capture.Image.Slot, effect_id = block.EffectId, reason = "incomplete-amp-fields-or-scene-state" });
                foreach (var field in types)
                {
                    used.Add(field.RawValue);
                    if (ampType != null && ampType != field.RawValue) continue;
                    uses.Add(new { slot = capture.Image.Slot, preset_name = capture.Image.Name, effect_id = block.EffectId, channel = field.Channel,
                        amp_type = field.RawValue, scenes = block.Scenes.Where(s => s.Channel == field.Channel)
                            .Select(s => new { scene = s.Scene, name = capture.Image.SceneNames[s.Scene - 1], bypassed = s.Bypassed }).ToArray() });
                }
            }
        }
        bool full = inventory.Complete && captures.Length == context.Definition.PresetCapacity && errors.Count == 0;
        return new { schema_version = 1, model = context.Definition.Model, firmware = context.Firmware, capacity = context.Definition.PresetCapacity,
            full_capacity_decoded = full, capture_complete = inventory.Complete, captured_slots = captures.Length, requested_slots = inventory.RequestedSlots.Length,
            missing_slots = inventory.RequestedSlots.Except(captures.Select(c => c.Image.Slot)).ToArray(), amp_type = ampType,
            uses = uses.Skip(offset).Take(limit ?? uses.Count).ToArray(), errors, offset, use_count = uses.Count,
            next_offset = offset + (limit ?? uses.Count) < uses.Count ? (int?)(offset + (limit ?? uses.Count)) : null,
            used_ids = used.Order().ToArray(), absent_ids = full && availableIds != null ? availableIds.Distinct().Except(used).Order().ToArray() : null,
            absence_status = availableIds == null ? "no-roster-supplied" : full ? "absent-from-complete-saved-content" : "unknown-incomplete-coverage",
            ports_opened = false, scope = "Saved channel usage including bypassed/unselected channels; not current live usage or audible identity." };
    }
}
