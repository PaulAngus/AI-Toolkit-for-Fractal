using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace Fractal.Core;

public sealed record CalibrationSample(double Value, string Sha256, string? Capture);
public sealed record ParameterCalibration(int SchemaVersion, string Model, int Capacity, string Firmware,
    int EffectId, string? Channel, string Key, string Unit, CalibrationSample[] Samples, string Kind = "parameter",
    int? ModifierSlot = null, int[]? ExpectedBinding = null);
public sealed record ResolvedCalibration(int RelativeOffset, int Columns, int Rows, string ContextSha256,
    double[] Values, int[] RawValues, string[] CaptureHashes, string Qualification);

/// <summary>Exact observed values only. No invented interpolation, extrapolation or editor-ID-to-file conversion.</summary>
public static class SavedCalibration
{
    public static ResolvedCalibration Resolve(ParameterCalibration calibration)
    {
        var d = DeviceDefinition.Create(calibration.Model, calibration.Capacity);
        _ = EditReadCatalogue.Capabilities(d, calibration.Firmware);
        if (calibration.SchemaVersion != 1 || calibration.EffectId is < 1 or > 1000 ||
            calibration.Key == null || !Regex.IsMatch(calibration.Key, @"\A[a-z][a-z0-9_.]{1,79}\z") ||
            string.IsNullOrWhiteSpace(calibration.Unit) || calibration.Unit.Length > 40 || calibration.Unit.Any(char.IsControl) ||
            calibration.Kind is not ("parameter" or "scene-ignore" or "modifier") || calibration.Channel is not (null or "A" or "B" or "C" or "D") ||
            calibration.Samples == null || calibration.Samples.Length is < 2 or > 16 ||
            calibration.Samples.Any(s => s == null || !double.IsFinite(s.Value) || s.Capture == null || s.Capture.Length > 80000) ||
            calibration.Samples.Select(s => s.Value).Distinct().Count() != calibration.Samples.Length)
            throw new InvalidDataException("Invalid calibration metadata or sample bounds.");
        if (calibration.Kind == "parameter" && calibration.Channel == null)
            throw new InvalidDataException("Parameter calibration requires an explicit channel.");
        if (calibration.Kind == "scene-ignore" && (calibration.Channel != null || calibration.Unit != "boolean" ||
            !calibration.Key.EndsWith(".scene_ignore", StringComparison.Ordinal) || !calibration.Samples.Select(s => s.Value).Order().SequenceEqual(new double[] { 0, 1 })))
            throw new InvalidDataException("Scene Ignore needs block-scoped off/on witnesses, boolean unit and a .scene_ignore key.");
        if (calibration.Kind == "modifier" && (calibration.Channel != null || calibration.ModifierSlot is null or < 1 or > 128 ||
            !calibration.Key.StartsWith("modifier.", StringComparison.Ordinal) || calibration.ExpectedBinding is not { Length: 3 } ||
            calibration.ExpectedBinding.Any(v => v is < 0 or > 65535) || calibration.ExpectedBinding[0] > 16383 ||
            calibration.ExpectedBinding[1] != calibration.EffectId || calibration.ExpectedBinding[2] > 16383))
            throw new InvalidDataException("Modifier calibration requires a slot, exact placed target binding and modifier key.");
        if (calibration.Kind != "modifier" && (calibration.ModifierSlot != null || calibration.ExpectedBinding != null))
            throw new InvalidDataException("Block calibration cannot carry modifier binding metadata.");
        var captures = calibration.Samples.Select(s =>
        {
            byte[] wire = Convert.FromBase64String(s.Capture!);
            if (wire.Length > 60000 || PresetBody.Sha256(wire) != s.Sha256) throw new InvalidDataException("Calibration capture hash mismatch.");
            return PresetEditProjection.Decode(d, calibration.Firmware, wire);
        }).ToArray();
        var bodies = captures.Select(c => Convert.FromHexString(c.BodyHex)).ToArray();
        if (calibration.Kind == "modifier") return ResolveModifier(calibration, d, captures, bodies);
        var records = captures.Select(c => c.Blocks.SingleOrDefault(b => b.EffectId == calibration.EffectId && b.Cells.Length == 1)?.Record
            ?? throw new InvalidDataException("Calibration requires a uniquely identified placed instance.")).ToArray();
        var record = records[0];
        if (record.Rows != 4 || records.Any(r => r != record) || captures.Any(c => c.Slot != captures[0].Slot) || bodies.Any(b => b.Length != bodies[0].Length))
            throw new InvalidDataException("Calibration witnesses must retain slot, exact instance, layout and body length.");
        var changedWords = Enumerable.Range(0, bodies[0].Length / 2).Where(i => bodies.Skip(1).Any(b => Word(b, i * 2) != Word(bodies[0], i * 2))).ToArray();
        if (changedWords.Length != 1 || bodies[0].Length % 2 != 0)
            throw new InvalidDataException("Witnesses must differ in exactly one saved uint16 word and preserve every other body byte.");
        int offset = changedWords[0] * 2, start = record.ShapeOffset + 34, end = record.PrefixOffset + record.ByteLength;
        if (calibration.Channel is string channel) { start += (channel[0] - 'A') * record.Columns * 2; end = start + record.Columns * 2; }
        if (offset < start || offset + 2 > end) throw new InvalidDataException("Calibrated word is outside the selected settings region; grid, headers and scene assignments are not calibration targets.");
        var mapped = captures[0].Blocks.Single(b => b.EffectId == calibration.EffectId).Parameters
            .Where(p => p.BodyOffset == offset || (p.Key == calibration.Key && p.Channel == calibration.Channel)).ToArray();
        if (mapped.Any(p => p.BodyOffset != offset || p.Key != calibration.Key || p.Unit != calibration.Unit || p.Scale == "identity"))
            throw new InvalidDataException("Calibration contradicts an existing mapped field identity or unit.");
        int[] raw = bodies.Select(b => Word(b, offset)).ToArray();
        if (raw.Distinct().Count() != raw.Length) throw new InvalidDataException("Different declared values must have distinct observed raw words.");
        return new(offset - record.ShapeOffset, record.Columns, record.Rows, Context(bodies[0], record, offset),
            calibration.Samples.Select(s => s.Value).ToArray(), raw, calibration.Samples.Select(s => s.Sha256).ToArray(),
            "caller-declared value labels; integrity-checked single-word witnesses; exact samples and matching block context only; no hardware or physical-unit qualification inferred");
    }
    private static ResolvedCalibration ResolveModifier(ParameterCalibration c, DeviceDefinition d, PresetEditReadReport[] captures, byte[][] bodies)
    {
        var slots = bodies.Select(b => SavedModifiers.Read(d, b).SingleOrDefault(m => m.Slot == c.ModifierSlot)
            ?? throw new InvalidDataException("Modifier slot is missing from a calibration witness.")).ToArray();
        var first = slots[0];
        var targets = captures.Select(x => x.Blocks.SingleOrDefault(b => b.EffectId == c.EffectId && b.Cells.Length == 1 && b.Record?.Rows == 4)?.Record
            ?? throw new InvalidDataException("Modifier target requires a unique placed instance.")).ToArray();
        if (captures.Any(x => x.Slot != captures[0].Slot) || bodies.Any(b => b.Length != bodies[0].Length) ||
            slots.Any(m => m.SettingsOffset != first.SettingsOffset || !c.ExpectedBinding!.SequenceEqual(new[] { m.SourceId, m.EffectId, m.ParameterId })) ||
            targets.Any(r => r != targets[0]))
            throw new InvalidDataException("Modifier witnesses must retain slot, layout, exact binding and unique placed target.");
        var changed = Enumerable.Range(0, bodies[0].Length / 2).Where(i => bodies.Skip(1).Any(b => Word(b, 2 * i) != Word(bodies[0], 2 * i))).ToArray();
        if (bodies[0].Length % 2 != 0 || changed.Length != 1)
            throw new InvalidDataException("Modifier witnesses must differ in exactly one saved uint16 word.");
        int offset = 2 * changed[0], word = (offset - first.SettingsOffset) / 2;
        if (offset < first.SettingsOffset || offset + 2 > first.SettingsOffset + 50 ||
            (offset - first.SettingsOffset) % 2 != 0 || word is 0 or 8 or 9)
            throw new InvalidDataException("Modifier binding and structural words are not calibration targets.");
        var catalogue = BuiltinScales.Catalogue(d, c.Firmware);
        var mapped = catalogue.ModifierPercentFields.Concat(catalogue.ModifierNormalizedFields).Concat(catalogue.ModifierRawSelectors)
            .ToDictionary(p => "modifier." + p.Key, p => p.Value);
        if (captures[0].Modifiers?.SingleOrDefault(m => m.Slot == c.ModifierSlot)?.NamedFields?.Any(f => f.Key == "modifier.minimum") == true)
        { mapped["modifier.minimum"] = 1; mapped["modifier.maximum"] = 2; }
        if (mapped.TryGetValue(c.Key, out int namedWord) ? namedWord != word : mapped.Values.Contains(word))
            throw new InvalidDataException("Modifier witness contradicts a source-mapped field identity.");
        int[] raw = bodies.Select(b => Word(b, offset)).ToArray();
        if (raw.Distinct().Count() != raw.Length) throw new InvalidDataException("Declared modifier values need distinct observed words.");
        return new(word * 2, 25, 1, ModifierContext(bodies[0], first.SettingsOffset, offset, targets[0]),
            c.Samples.Select(s => s.Value).ToArray(), raw, c.Samples.Select(s => s.Sha256).ToArray(),
            "caller-declared modifier display labels; integrity-checked single-word witnesses, exact binding and matching 25-word context only; no interpolation or hardware/physical-unit qualification inferred");
    }
    public static object ApplyModifier(DeviceDefinition d, string firmware, byte[] body, PresetEditReadReport before, PresetEditOperation operation)
    {
        var c = operation.Calibration ?? throw new ArgumentException("calibration is required.");
        var r = Resolve(c);
        var slot = SavedModifiers.Read(d, body).SingleOrDefault(m => m.Slot == operation.ModifierSlot)
            ?? throw new InvalidDataException("Modifier slot is missing.");
        var target = before.Blocks.SingleOrDefault(b => b.EffectId == slot.EffectId && b.Cells.Length == 1 && b.Record?.Rows == 4)?.Record;
        if (c.Kind != "modifier" || c.Model != d.Model || c.Capacity != d.PresetCapacity || c.Firmware != firmware ||
            c.ModifierSlot != operation.ModifierSlot || c.EffectId != slot.EffectId || c.Key != operation.Key || c.Unit != operation.Unit ||
            operation.ExpectedBinding == null || !operation.ExpectedBinding.SequenceEqual(c.ExpectedBinding!) ||
            !operation.ExpectedBinding.SequenceEqual(new[] { slot.SourceId, slot.EffectId, slot.ParameterId }) || target == null)
            throw new InvalidDataException("Modifier calibration context differs from the requested operation.");
        int offset = slot.SettingsOffset + r.RelativeOffset;
        if (r.Columns != 25 || r.Rows != 1 || ModifierContext(body, slot.SettingsOffset, offset, target) != r.ContextSha256)
            throw new InvalidDataException("Modifier settings differ from the witnessed context.");
        int index = Array.IndexOf(r.Values, operation.CalibratedValue ?? double.NaN), old = Word(body, offset);
        if (index < 0 || !r.RawValues.Contains(old))
            throw new InvalidDataException("Requested/current modifier value lacks an exact witness.");
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)r.RawValues[index]);
        return new { modifier_slot = slot.Slot, key = c.Key, requested = operation.CalibratedValue, unit = c.Unit,
            raw_before = old, raw_after = r.RawValues[index], relative_offset = r.RelativeOffset,
            witness_hashes = r.CaptureHashes, context_sha256 = r.ContextSha256, qualification = r.Qualification };
    }
    public static object Apply(DeviceDefinition d, string firmware, byte[] body, SavedBlockRead block, PresetEditOperation operation)
    {
        var c = operation.Calibration ?? throw new ArgumentException("calibration is required.");
        var r = Resolve(c);
        if (c.Model != d.Model || c.Capacity != d.PresetCapacity || c.Firmware != firmware || c.EffectId != block.EffectId ||
            c.Key != operation.Key || c.Channel != operation.Channel || c.Unit != operation.Unit ||
            c.Kind != (operation.Kind == "set-scene-ignore" ? "scene-ignore" : "parameter"))
            throw new InvalidDataException("Calibration context differs from the requested operation.");
        var record = block.Record!; int offset = record.ShapeOffset + r.RelativeOffset;
        if (record.Columns != r.Columns || record.Rows != r.Rows || Context(body, record, offset) != r.ContextSha256)
            throw new InvalidDataException("Block settings differ from the witnessed calibration context.");
        double value = operation.Kind == "set-scene-ignore" ? operation.Enabled == true ? 1 : operation.Enabled == false ? 0 : double.NaN : operation.CalibratedValue ?? double.NaN;
        int index = Array.IndexOf(r.Values, value), old = Word(body, offset);
        if (index < 0 || !r.RawValues.Contains(old)) throw new InvalidDataException("Requested/current value has no exact calibration witness; no interpolation or extrapolation.");
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), (ushort)r.RawValues[index]);
        return new { key = c.Key, requested = value, unit = c.Unit, raw_before = old, raw_after = r.RawValues[index],
            relative_offset = r.RelativeOffset, witness_hashes = r.CaptureHashes, context_sha256 = r.ContextSha256, qualification = r.Qualification };
    }
    private static int Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static string ModifierContext(byte[] body, int settingsOffset, int offset, InstanceRecordRead target)
    {
        var bytes = body.AsSpan(settingsOffset, 50).ToArray();
        bytes.AsSpan(offset - settingsOffset, 2).Clear();
        return PresetBody.Sha256(bytes.Concat(body.AsSpan(target.PrefixOffset, target.ByteLength).ToArray()).ToArray());
    }
    private static string Context(byte[] body, InstanceRecordRead record, int offset)
    {
        var bytes = body.AsSpan(record.PrefixOffset, record.ByteLength).ToArray();
        bytes.AsSpan(offset - record.PrefixOffset, 2).Clear(); return PresetBody.Sha256(bytes);
    }
}
