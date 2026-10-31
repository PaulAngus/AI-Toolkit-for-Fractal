using System.Buffers.Binary;

namespace Fractal.Core;

public sealed record ModifierValue(string Key, int RawValue, double? Value, SavedValueScale Scale);
public sealed record ModifierRawSelector(string Key, int RawValue);
public sealed record SavedModifier(int Slot, int SettingsOffset, int SourceId, int EffectId, int ParameterId, string RawHex,
    ModifierValue[]? NamedFields = null, ModifierRawSelector[]? RawSelectors = null);

/// <summary>Source-qualified Gen3 25x1 records. Preserve unknown curve words and initialize only from explicit templates.</summary>
public static class SavedModifiers
{
    public static SavedModifier[] Read(DeviceDefinition device, byte[] body, string? firmware = null, SavedBlockRead[]? blocks = null) => PresetBody.ReadLayout(device, body).Records
        .Where(r => r.Columns == 25 && r.Rows == 1).Select((r, i) => new SavedModifier(i + 1, r.Offset + 34,
            Word(body, r.Offset + 34), Word(body, r.Offset + 50), Word(body, r.Offset + 52), Convert.ToHexString(body.AsSpan(r.Offset + 34, 50))))
        .Select(m => m with { NamedFields = firmware == null ? [] : Fields(device, firmware, m, blocks).Select(f =>
            new ModifierValue(f.Key, Word(body, m.SettingsOffset + 2 * f.Word), f.Scale.Decode(Word(body, m.SettingsOffset + 2 * f.Word)), f.Scale)).ToArray(),
            RawSelectors = firmware == null ? [] : BuiltinScales.Catalogue(device, firmware).ModifierRawSelectors.Select(pair =>
                new ModifierRawSelector("modifier." + pair.Key, Word(body, m.SettingsOffset + 2 * pair.Value))).ToArray() }).ToArray();
    private static IEnumerable<(string Key, int Word, SavedValueScale Scale)> Fields(DeviceDefinition device, string firmware, SavedModifier slot, SavedBlockRead[]? blocks)
    {
        var catalogue = BuiltinScales.Catalogue(device, firmware);
        foreach (var pair in catalogue.ModifierPercentFields)
            yield return ("modifier." + pair.Key, pair.Value, new(0, 100, "%", "linear", 65534));
        foreach (var pair in catalogue.ModifierNormalizedFields)
            yield return ("modifier." + pair.Key, pair.Value, new(0, 1, "saved-normalized", "linear", 65534));
        var block = blocks?.SingleOrDefault(b => b.EffectId == slot.EffectId && b.Cells.Length == 1 && b.Record?.Rows == 4);
        var target = catalogue.Fields.SingleOrDefault(f => f.EffectIds.Contains(slot.EffectId) && f.ParameterId == slot.ParameterId && f.Columns == block?.Record?.Columns);
        // Integer target endpoints have no established modifier-word representation.
        if (target != null && target.Scale.Taper != "int-raw")
        {
            yield return ("modifier.minimum", 1, target.Scale);
            yield return ("modifier.maximum", 2, target.Scale);
        }
    }
    public static object ApplyParameter(DeviceDefinition device, string firmware, byte[] body, PresetEditReadReport before, PresetEditOperation operation)
    {
        var slot = Read(device, body).SingleOrDefault(m => m.Slot == operation.ModifierSlot)
            ?? throw new InvalidDataException("Modifier slot is not present.");
        if (slot.EffectId == 0 || operation.ExpectedBinding == null || operation.ExpectedBinding.Length != 3 || operation.ExpectedBinding.Any(v => v is < 0 or > 65535))
            throw new InvalidDataException("Named curve editing requires an existing exact expected_binding.");
        if (!before.Blocks.Any(b => b.EffectId == slot.EffectId && b.Cells.Length == 1 && b.Record != null))
            throw new InvalidDataException("Modifier target requires a unique placed instance.");
        var matches = Fields(device, firmware, slot, before.Blocks).Where(f => f.Key == operation.Key).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("No named modifier scale for this model/firmware or target parameter.");
        var field = matches[0];
        if (operation.PhysicalValue == null || operation.Unit != field.Scale.Unit) throw new ArgumentException("Specify physical_value and the named field's unit.");
        int offset = slot.SettingsOffset + field.Word * 2, old = Word(body, offset), raw = field.Scale.Encode(operation.PhysicalValue.Value);
        bool sameBinding = operation.ExpectedBinding.SequenceEqual(new[] { slot.SourceId, slot.EffectId, slot.ParameterId });
        // A repeated curve+source plan may already have its desired source. Never change a curve under a different owner.
        if (!sameBinding && !(old == raw && operation.ExpectedBinding[1] == slot.EffectId && operation.ExpectedBinding[2] == slot.ParameterId))
            throw new InvalidDataException("Named modifier binding differs from the expected ownership precondition.");
        Put(body, offset, raw);
        return new { modifier_slot = slot.Slot, field.Key, requested = operation.PhysicalValue, before = field.Scale.Decode(old),
            effective_saved_value = field.Scale.Decode(raw), raw_before = old, raw_after = raw, field.Scale,
            source_ids = new[] { "axe32-modifiers", "axe32-word-laws", "gen3-body" },
            catalogue_sha256 = BuiltinScales.Catalogue(device, firmware).CatalogueSha256,
            qualification = "Source-derived 25-word saved modifier layout and upstream-reported normalization; hardware testing separate. saved-normalized is a wire fraction, not an editor display or time unit. Min may exceed Max for reversed control; no curve defaults applied." };
    }
    public static object CopyRawSelector(DeviceDefinition device, string firmware, byte[] body, PresetEditReadReport before, PresetEditOperation operation)
    {
        var slot = Read(device, body).SingleOrDefault(m => m.Slot == operation.ModifierSlot)
            ?? throw new InvalidDataException("Modifier slot is not present.");
        var binding = new[] { slot.SourceId, slot.EffectId, slot.ParameterId };
        if (slot.EffectId == 0 || operation.ExpectedBinding == null || !operation.ExpectedBinding.SequenceEqual(binding) ||
            !before.Blocks.Any(b => b.EffectId == slot.EffectId && b.Cells.Length == 1 && b.Record != null))
            throw new InvalidDataException("Raw selector copy requires the exact existing placed modifier binding.");
        var selectors = BuiltinScales.Catalogue(device, firmware).ModifierRawSelectors;
        string key = operation.Key?.StartsWith("modifier.", StringComparison.Ordinal) == true ? operation.Key[9..] : "";
        if (!selectors.TryGetValue(key, out int word))
            throw new InvalidDataException("No raw modifier selector for this model, firmware and key.");
        if (operation.TemplateCapture == null || operation.TemplateCapture.Length > 80000 || operation.TemplateFirmware != firmware)
            throw new ArgumentException("A complete same-model/firmware selector witness is required.");
        byte[] wire = Convert.FromBase64String(operation.TemplateCapture);
        if (wire.Length > 60000 || PresetBody.Sha256(wire) != operation.TemplateSha256)
            throw new InvalidDataException("Selector witness hash mismatch.");
        var donor = SnapshotExport.DecodeFile(device, wire);
        var template = Read(device, donor.Image.Body).SingleOrDefault(m => m.Slot == operation.TemplateModifierSlot)
            ?? throw new InvalidDataException("Selector witness modifier slot is missing.");
        if (template.SourceId != slot.SourceId || template.EffectId != slot.EffectId || template.ParameterId != slot.ParameterId)
            throw new InvalidDataException("Selector witness binding differs from the target binding.");
        int offset = slot.SettingsOffset + 2 * word, old = Word(body, offset);
        int desired = Word(Convert.FromHexString(template.RawHex), 2 * word);
        Put(body, offset, desired);
        return new { modifier_slot = slot.Slot, operation.Key, raw_before = old, raw_after = desired,
            witness_sha256 = operation.TemplateSha256, witness_modifier_slot = template.Slot,
            qualification = "Exact same-model/firmware, same-binding raw selector witness; only its saved word is copied. Selector index meaning and effective damping behavior are not inferred." };
    }
    public static object Apply(DeviceDefinition device, string firmware, byte[] body, PresetEditOperation operation)
    {
        var slots = Read(device, body);
        var slot = slots.SingleOrDefault(m => m.Slot == operation.ModifierSlot) ?? throw new InvalidDataException("Modifier slot is not present in the parsed chain.");
        if (operation.ExpectedBinding == null || operation.ExpectedBinding.Length != 3 || operation.ExpectedBinding.Any(v => v is < 0 or > 65535))
            throw new InvalidDataException("Modifier requires expected_binding [source, effect, parameter].");
        byte[] desired = Convert.FromHexString(slot.RawHex);
        if (operation.Kind == "set-modifier-source")
        {
            if (operation.SourceId is null or < 0 or > 16383 || slot.EffectId == 0)
                throw new ArgumentException("An existing binding and explicit numeric source_id 0–16383 are required.");
            Put(desired, 0, operation.SourceId.Value);
        }
        else
        {
            if (operation.TemplateCapture == null || operation.TemplateCapture.Length > 80000 || operation.TemplateFirmware != firmware)
                throw new ArgumentException("Modifier authoring requires a complete same-model/firmware template.");
            byte[] wire = Convert.FromBase64String(operation.TemplateCapture);
            if (PresetBody.Sha256(wire) != operation.TemplateSha256) throw new InvalidDataException("Modifier template hash mismatch.");
            var donor = SnapshotExport.DecodeFile(device, wire);
            var template = Read(device, donor.Image.Body).SingleOrDefault(m => m.Slot == operation.TemplateModifierSlot)
                ?? throw new InvalidDataException("Template modifier slot missing.");
            if (template.EffectId == 0 || template.SourceId > 16383 || template.ParameterId > 16383)
                throw new InvalidDataException("Template requires an explicit supported binding.");
            desired = Convert.FromHexString(template.RawHex);
        }
        int target = Word(desired, 16), parameter = Word(desired, 18);
        if (!operation.ExpectedBinding.SequenceEqual(new[] { slot.SourceId, slot.EffectId, slot.ParameterId }) &&
            !desired.SequenceEqual(Convert.FromHexString(slot.RawHex)))
            throw new InvalidDataException("Modifier binding differs from the expected ownership precondition.");
        if (!PresetBody.ReadLayout(device, body).Grid.Any(c => c.EffectId == target)) throw new InvalidDataException("Modifier target is not placed.");
        if (slots.Any(m => m.Slot != slot.Slot && m.EffectId == target && m.ParameterId == parameter))
            throw new InvalidDataException("A different modifier slot already targets this parameter.");
        desired.CopyTo(body, slot.SettingsOffset);
        return new { modifier_slot = slot.Slot, before = new { slot.SourceId, slot.EffectId, slot.ParameterId },
            after = new { source_id = Word(desired, 0), effect_id = target, parameter_id = parameter },
            before_settings_sha256 = PresetBody.Sha256(Convert.FromHexString(slot.RawHex)), after_settings_sha256 = PresetBody.Sha256(desired),
            qualification = "gen3-body open-implementation 25x1 layout; exact numeric binding, opaque curve preserved/copied; no inferred physical curve units or new hardware qualification" };
    }
    private static int Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static void Put(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), checked((ushort)value));
}
