using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fractal.Core;

public sealed record PresetEditOperation(string Kind, int? EffectId = null, string? Channel = null,
    string? Key = null, double? Value = null, string? Unit = null, int? Scene = null, string? Name = null,
    bool? Bypassed = null, int? Row = null, int? Column = null, int? SourceRow = null, int? SourceColumn = null,
    int? DestinationRow = null, int? DestinationColumn = null, string? Policy = null,
    string? TemplateCapture = null, string? TemplateSha256 = null, string? TemplateFirmware = null, string? TemplateChannel = null,
    int? MessageSlot = null, string? MessageType = null, int? MidiChannel = null, int? Controller = null, int[]? ExpectedRaw = null,
    ParameterCalibration? Calibration = null, bool? Enabled = null, int? ModifierSlot = null, int? SourceId = null, int? TemplateModifierSlot = null, double? CalibratedValue = null, int[]? ExpectedBinding = null, double? PhysicalValue = null,
    int[]? MessageRows = null, int[]? ExpectedRowsRaw = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OtherChannel = null);
public sealed record PresetEditRequest(int SchemaVersion, string Model, int Capacity, string Firmware, int Slot,
    string Persistence, string BaseSha256, PresetEditOperation[] Operations, string FeedbackPolicy = "preserve");
public sealed record PresetEditItem(PresetEditOperation Operation, string Status, string Code, string Detail, object? Change = null);

/// <summary>Deterministic complete-image authoring. The only editable fields come from contained semantic profiles.</summary>
public sealed class PresetEditPlan
{
    public static readonly JsonSerializerOptions RequestJson = new(SnapshotExport.JsonOptions)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly byte[] baseline, expected;
    private readonly PresetEditRequest request;
    private readonly PresetEditItem[] items;
    public DeviceDefinition Device { get; }
    public string Firmware => request.Firmware;
    public int Slot => request.Slot;
    public int SavedScene { get; }
    public int BeforeScene => Word(SnapshotExport.DecodeFile(Device, baseline).Image.Body, 0) + 1;
    public bool OrganizationOnly => request.Operations.All(o => o.Kind is "rename-preset" or "copy-preset");
    public string Persistence => request.Persistence;
    public bool OnlySceneMidiOperations => request.Operations.All(o => o.Kind is "set-scene-midi" or "restore-scene-midi-rows");
    public bool Executable => items.All(i => i.Status is "planned" or "unchanged");
    public bool Changed => Executable && !baseline.SequenceEqual(expected);
    public string BaseSha256 => PresetBody.Sha256(baseline);
    public string ExpectedSha256 => PresetBody.Sha256(expected);
    public byte[] Baseline => baseline.ToArray();
    public byte[] Expected => expected.ToArray();
    public string PlanSha256 => PresetBody.Sha256(Encoding.UTF8.GetBytes(SnapshotExport.Json(Report())));
    private PresetEditPlan(DeviceDefinition device, PresetEditRequest request, byte[] baseline, byte[] expected, PresetEditItem[] items, int scene)
    { Device = device; this.request = request; this.baseline = baseline; this.expected = expected; this.items = items; SavedScene = scene; }
    public object Report() => new { schema_version = 1, protocol_release = ProtocolRelease.Version, model = Device.Model,
        capacity = Device.PresetCapacity, firmware = Firmware, slot = Slot, persistence = Persistence, saved_scene = SavedScene,
        base_sha256 = BaseSha256, expected_sha256 = ExpectedSha256, profile = EditReadCatalogue.Capabilities(Device, Firmware).Scope,
        status = !Executable ? "conflict" : Changed ? "planned" : "unchanged", executable = Executable,
        items = items.Select(i => i with { Operation = i.Operation with { TemplateCapture = null,
            Calibration = i.Operation.Calibration is { } c ? c with { Samples = (c.Samples ?? []).Where(s => s != null).Select(s => s with { Capture = null }).ToArray() } : null } }).ToArray(),
        routing = RoutingReview(), feedback_policy = request.FeedbackPolicy,
        execution_method = "complete-image-temporary-upload", hardware_qualified = false, ports_opened = false,
        limitations = new[] { "Saved-value editing; modifiers and Scene Ignore are changed only by explicit operations, effective live behavior is not inferred.",
            "Capture calibrations accept exact witnessed values and matching block context; caller-declared labels do not establish hardware calibration.",
            "Source-derived model profiles; physical qualification is recorded separately. No firmware-wide disable gate.",
            "Grid cables plus documented Send/Return pairs and I/O roles. Reachability is structural; levels, bypass and global I/O configuration determine audibility.",
            "Type changes explicitly preserve other stored settings; template insertion copies the complete selected instance." } };
    private object RoutingReview()
    {
        var beforeRead = PresetEditProjection.Decode(Device, Firmware, baseline);
        var afterRead = PresetEditProjection.Decode(Device, Firmware, expected);
        var before = beforeRead.Routing; var after = afterRead.Routing;
        return new { before_nodes = before.Nodes, after_nodes = after.Nodes, before_edges = before.Edges, after_edges = after.Edges,
            before_special = beforeRead.SpecialRouting, after_special = afterRead.SpecialRouting,
            changed_cells = before.Words.Zip(after.Words).Where(p => p.First.EffectId != p.Second.EffectId || p.First.RawWord != p.Second.RawWord)
                .Select(p => new { p.First.Row, p.First.Column, before_effect_id = p.First.EffectId, after_effect_id = p.Second.EffectId,
                    before_word = p.First.RawWord, after_word = p.Second.RawWord }).ToArray() };
    }
    public static PresetEditRequest Parse(byte[] json)
    {
        if (json.Length > 4_000_000) throw new InvalidDataException("Edit request exceeds size bound.");
        try { return JsonSerializer.Deserialize<PresetEditRequest>(json, RequestJson) ?? throw new InvalidDataException("Missing edit request."); }
        catch (JsonException e) { throw new InvalidDataException("Invalid edit request JSON.", e); }
    }
    internal async Task VerifyLiveNamesAsync(ReadSession session, DeviceContext context, TimeSpan timeout, CancellationToken token, bool undo)
    {
        var wire = undo ? baseline : expected;
        var captured = SnapshotExport.DecodeFile(Device, wire);
        var before = await session.CurrentAsync(context, timeout, token);
        if (OrganizationOnly && before.Name != captured.Image.Name) throw new InvalidDataException("Independent current preset name differs from the reviewed image.");
        if (request.Operations.Any(o => o.Kind == "copy-preset"))
            for (int scene = 1; scene <= 8; scene++)
                if (await session.SceneNameAsync(context, scene, timeout, token) != captured.Image.SceneNames[scene - 1])
                    throw new InvalidDataException("Independent copied scene-name readback differs.");
        foreach (int scene in request.Operations.Where(o => o.Kind == "rename-scene").Select(o => o.Scene!.Value).Distinct())
            if (await session.SceneNameAsync(context, scene, timeout, token) != captured.Image.SceneNames[scene - 1])
                throw new InvalidDataException("Independent live scene-name readback differs from the authored image.");
        if (request.Operations.Any(o => o.Kind is "set-scene-midi" or "restore-scene-midi-rows" || o.EffectId == 190 && o.Kind is "insert-block" or "copy-block-settings") &&
            PresetBody.ReadLayout(Device, captured.Image.Body).Grid.Any(c => c.EffectId == 190))
        {
            var fields = SceneMidiTable.SavedFields(Device, PresetBody.ReadLayout(Device, captured.Image.Body).Records);
            if (!(await session.SceneMidiAsync(context, timeout, token)).Fields.SequenceEqual(fields))
                throw new InvalidDataException("Independent live Scene MIDI table differs from the authored image.");
        }
        if (before.Slot != Slot || before.Scene != (undo ? BeforeScene : SavedScene) || before != await session.CurrentAsync(context, timeout, token))
            throw new InvalidDataException("Selection changed around live name verification.");
    }
    public static PresetEditPlan Create(PresetEditRequest input, byte[] savedCapture)
    {
        // Freeze caller-owned arrays and normalize operation ordering before deriving any result.
        var request = Parse(JsonSerializer.SerializeToUtf8Bytes(input, RequestJson));
        var d = DeviceDefinition.Create(request.Model, request.Capacity);
        _ = EditReadCatalogue.Capabilities(d, request.Firmware); d.ValidateSlot(request.Slot);
        if (request.SchemaVersion != 1 || request.Persistence is not ("save" or "edit-buffer") || request.Operations == null ||
            request.Operations.Length is < 1 or > 256 || request.Operations.Any(o => o == null)) throw new ArgumentException("Invalid edit request scope.");
        if (request.Operations.Any(o => o.Kind == "restore-scene-midi-rows") && request.Operations.Length != 1)
            throw new ArgumentException("A backup-sourced Scene MIDI row restoration must be the only edit operation.");
        if (request.FeedbackPolicy is not ("preserve" or "allow")) throw new ArgumentException("feedback_policy must be preserve or allow.");
        byte[] baseline = savedCapture.ToArray();
        var original = SnapshotExport.DecodeFile(d, baseline);
        if (original.Image.Slot != request.Slot || PresetBody.Sha256(baseline) != request.BaseSha256)
            throw new InvalidDataException("Edit baseline slot/hash mismatch.");
        int scene = Word(original.Image.Body, 0) + 1;
        if (scene is < 1 or > 8) throw new InvalidDataException("Saved default scene field is outside 1–8.");
        if (request.Operations.Any(o => o.Kind is "rename-preset" or "copy-preset"))
            return CreateOrganizationImage(d, request, baseline);
        var operations = request.Operations.OrderBy(Phase).ThenBy(o => SnapshotExport.Json(o), StringComparer.Ordinal)
            .GroupBy(o => SnapshotExport.Json(o), StringComparer.Ordinal).Select(g => g.First()).ToArray();
        request = request with { Operations = operations };
        byte[] body = original.Image.Body.ToArray(); var items = new List<PresetEditItem>();
        var assignments = new Dictionary<string, string>();
        // A swap owns the selected instance's channel rows and optional scene references.
        // Refuse same-instance composition instead of silently ordering overlapping writes.
        foreach (var swap in operations.Where(o => o.Kind == "swap-channels"))
            if (operations.Any(o => o != swap && (o.EffectId == swap.EffectId ||
                o.Calibration?.EffectId == swap.EffectId ||
                o.Kind?.Contains("modifier", StringComparison.Ordinal) == true)))
                items.Add(new(swap, "conflict", "overlapping-swap", "A channel swap must be the only settings/structure operation for its instance and cannot compose with shared modifier edits."));
        // Resolve conflicting intents before applying any operation; equivalent repeated requests collapse.
        foreach (var op in operations)
        {
            var targets = op.Kind == "set-scene-block-state" ? new[] {
                (Key: Target(op) + ":channel", Value: op.Channel),
                (Key: Target(op) + ":bypass", Value: op.Bypassed?.ToString()) }.Where(t => t.Value != null) :
                [(Target(op), (string?)SnapshotExport.Json(op))];
            foreach (var (key, value) in targets)
                if (assignments.TryGetValue(key, out string? prior) && prior != value)
                    items.Add(new(op, "conflict", "conflicting-assignment", "Multiple different assignments target the same field, instance or edge."));
                else assignments[key] = value!;
        }
        if (items.Count > 0) return new(d, request, baseline, baseline, items.ToArray(), scene);
        PresetEditReadReport Projection() => PresetEditProjection.Decode(d, request.Firmware, PresetBodyWriter.ReplaceBody(d, baseline, body));
        var initial = Projection();
        var calibratedAssignments = new Dictionary<int, int>();
        foreach (var op in operations)
        {
            byte[] previous = body.ToArray();
            try
            {
                ValidateFields(op);
                var before = Projection(); object? change = null;
                SavedBlockRead Block(int id)
                {
                    var block = before.Blocks.SingleOrDefault(b => b.EffectId == id && b.Record != null && b.Cells.Length == 1)
                        ?? throw new InvalidDataException("Block requires a unique grid cell and exact instance record.");
                    if (block.Record!.PrefixOffset < 0x104 + d.GridRows * d.GridColumns * 4)
                        throw new InvalidDataException("Instance interval overlaps the routing grid.");
                    return block;
                }
                int Id() => op.EffectId ?? throw new ArgumentException("effect_id is required.");
                int Channel() => op.Channel is "A" or "B" or "C" or "D" ? op.Channel[0] - 'A' : throw new ArgumentException("Use channel A, B, C or D.");
                int Cell(int? row, int? column)
                {
                    if (row is null || column is null || row < 1 || row > d.GridRows || column < 1 || column > d.GridColumns)
                        throw new ArgumentException("Cell is outside the model grid.");
                    return 0x104 + ((column.Value - 1) * d.GridRows + row.Value - 1) * 4;
                }
                void KnownRoute(int offset)
                { if ((Word(body, offset + 2) & ~((1 << d.GridRows) - 1)) != 0) throw new InvalidDataException("Edited routing word contains unknown bits."); }
                void DisconnectCell(int row, int column)
                {
                    int offset = Cell(row, column); KnownRoute(offset); Put(body, offset + 2, 0);
                    if (column < d.GridColumns) for (int r = 1; r <= d.GridRows; r++)
                    {
                        int dest = Cell(r, column + 1);
                        if ((Word(body, dest + 2) & (1 << (row - 1))) != 0)
                        { KnownRoute(dest); Put(body, dest + 2, Word(body, dest + 2) & ~(1 << (row - 1))); }
                    }
                }
                switch (op.Kind)
                {
                    case "set-scaled-parameter":
                        change = BuiltinScales.Apply(d, request.Firmware, body, Block(Id()), op); break;
                    case "set-modifier-parameter":
                        change = SavedModifiers.ApplyParameter(d, request.Firmware, body, before, op); break;
                    case "set-calibrated-modifier":
                    {
                        change = SavedCalibration.ApplyModifier(d, request.Firmware, body, before, op);
                        var slot = SavedModifiers.Read(d, body).Single(m => m.Slot == op.ModifierSlot);
                        int offset = slot.SettingsOffset + SavedCalibration.Resolve(op.Calibration!).RelativeOffset;
                        int desired = Word(body, offset);
                        if (calibratedAssignments.TryGetValue(offset, out int prior) && prior != desired)
                            throw new InvalidDataException("Different calibrated intents target the same saved word.");
                        calibratedAssignments[offset] = desired; break;
                    }
                    case "copy-modifier-selector":
                        change = SavedModifiers.CopyRawSelector(d, request.Firmware, body, before, op); break;
                    case "set-calibrated-parameter":
                    case "set-scene-ignore":
                    {
                        var block = Block(Id());
                        change = SavedCalibration.Apply(d, request.Firmware, body, block, op);
                        int offset = block.Record!.ShapeOffset + SavedCalibration.Resolve(op.Calibration!).RelativeOffset;
                        int desired = Word(body, offset);
                        if (calibratedAssignments.TryGetValue(offset, out int prior) && prior != desired)
                            throw new InvalidDataException("Different calibrated intents target the same saved word.");
                        calibratedAssignments[offset] = desired; break;
                    }
                    case "set-modifier-source":
                    case "copy-modifier":
                        change = SavedModifiers.Apply(d, request.Firmware, body, op); break;
                    case "set-scene-midi":
                    {
                        if (op.Scene is null or < 1 or > 8 || op.MessageSlot is null or < 1 or > 8 || op.MessageType is not ("Off" or "PC" or "CC"))
                            throw new ArgumentException("Specify scene/message_slot 1–8 and message_type Off, PC or CC.");
                        var record = Block(190).Record!; var layout = d.SceneMidi!;
                        if (record.Columns != layout.Columns || record.Rows != layout.Rows) throw new InvalidDataException("Scene MIDI record shape mismatch.");
                        int index = (op.Scene.Value - 1) * 8 + op.MessageSlot.Value - 1;
                        int basis = record.ShapeOffset + layout.FirstWord * 2;
                        int[] offsets = new[] { layout.TypeBank, layout.ChannelBank, layout.ControllerBank, layout.ValueBank }.Select(bank => basis + 2 * (bank + index)).ToArray();
                        int[] old = offsets.Select(offset => Word(body, offset)).ToArray(); int[] desired = old.ToArray();
                        if (op.MessageType == "Off")
                        {
                            if (op.MidiChannel != null || op.Controller != null || op.Value != null || op.Policy != null)
                                throw new ArgumentException("Off clears only type; supply expected_raw for an active message.");
                            desired[0] = 0;
                            if (old[0] != 0 && (op.ExpectedRaw == null || !op.ExpectedRaw.SequenceEqual(old)))
                                throw new InvalidDataException("Active message differs from the exact expected_raw ownership precondition.");
                        }
                        else
                        {
                            if (op.MidiChannel is null or < 1 or > 16 || op.Value is null or < 0 or > 127 || op.Value != Math.Truncate(op.Value.Value) ||
                                (op.MessageType == "CC" && op.Controller is null or < 0 or > 127) || op.MessageType == "PC" && op.Controller != null)
                                throw new ArgumentException("PC/CC needs MIDI channel 1–16 and integer value 0–127; CC also needs controller 0–127.");
                            desired[0] = op.MessageType == "CC" ? 1 : 2; desired[1] = op.MidiChannel.Value - 1; desired[3] = (int)op.Value.Value;
                            if (op.Controller != null) desired[2] = op.Controller.Value;
                            if (op.ExpectedRaw != null && !op.ExpectedRaw.SequenceEqual(old)) throw new InvalidDataException("Message precondition mismatch.");
                            if (old[0] != 0 && !old.SequenceEqual(desired) && op.Policy != "replace") throw new InvalidDataException("Occupied message requires explicit replace policy.");
                            if (op.Policy is not (null or "replace")) throw new ArgumentException("Use replace policy for an occupied message.");
                        }
                        if (op.ExpectedRaw != null && (op.ExpectedRaw.Length != 4 || op.ExpectedRaw.Any(v => v is < 0 or > 65535))) throw new ArgumentException("expected_raw contains exactly type/channel/controller/value uint16 words.");
                        for (int i = 0; i < 4; i++) Put(body, offsets[i], desired[i]);
                        _ = SceneMidiTable.Decode(d, SceneMidiTable.SavedFields(d, PresetBody.ReadLayout(d, body).Records));
                        change = new { scene = op.Scene, message_slot = op.MessageSlot, before_raw = old, after_raw = desired,
                            scope = "selected-message-only; inactive-fields-preserved" }; break;
                    }
                    case "restore-scene-midi-rows":
                    {
                        if (op.MessageRows is not { Length: >= 1 and <= 64 } rows || rows.Any(row => row is < 1 or > 64) ||
                            rows.Distinct().Count() != rows.Length || op.ExpectedRowsRaw is not { } expectedRows ||
                            expectedRows.Length != rows.Length * 4 || expectedRows.Any(word => word is < 0 or > 65535) ||
                            op.TemplateCapture == null || op.TemplateFirmware != request.Firmware)
                            throw new ArgumentException("Restore exact distinct Scene MIDI rows from one same-firmware saved backup with four expected words per row.");
                        byte[] donorWire = Convert.FromBase64String(op.TemplateCapture);
                        if (donorWire.Length > 60000 || PresetBody.Sha256(donorWire) != op.TemplateSha256)
                            throw new InvalidDataException("Scene MIDI row donor hash mismatch.");
                        var donor = SnapshotExport.DecodeFile(d, donorWire);
                        if (donor.Image.Slot != request.Slot)
                            throw new InvalidDataException("Scene MIDI row donor targets another preset slot.");
                        var currentBlock = Block(190);
                        var layout = d.SceneMidi ?? throw new InvalidDataException("Scene MIDI layout is unavailable.");
                        var donorBlocks = PresetEditProjection.Decode(d, request.Firmware, donorWire).Blocks
                            .Where(b => b.EffectId == 190 && b.Record != null && b.Cells.Length == 1).ToArray();
                        if (donorBlocks.Length != 1) throw new InvalidDataException("Scene MIDI row donor has no unique placed instance.");
                        var currentRecord = currentBlock.Record!; var donorRecord = donorBlocks[0].Record!;
                        if (currentRecord.ShapeOffset != donorRecord.ShapeOffset ||
                            currentRecord.Columns != donorRecord.Columns || currentRecord.Rows != donorRecord.Rows ||
                            !currentBlock.Cells.SequenceEqual(donorBlocks[0].Cells) ||
                            currentRecord.Columns != layout.Columns || currentRecord.Rows != layout.Rows)
                            throw new InvalidDataException("Scene MIDI row donor layout or placement differs from the current preset.");
                        int currentStart = currentRecord.ShapeOffset + layout.FirstWord * 2;
                        int donorStart = donorRecord.ShapeOffset + layout.FirstWord * 2;
                        int[] banks = [layout.TypeBank, layout.ChannelBank, layout.ControllerBank, layout.ValueBank];
                        foreach (var (row, index) in rows.Select((row, index) => (row, index)))
                        {
                            for (int bank = 0; bank < 4; bank++)
                            {
                                int offset = currentStart + 2 * (banks[bank] + row - 1);
                                if (Word(body, offset) != expectedRows[index * 4 + bank])
                                    throw new InvalidDataException($"Scene MIDI row {row} differs from its exact expected raw words.");
                            }
                        }
                        foreach (int row in rows)
                            foreach (int bank in banks)
                                Put(body, currentStart + 2 * (bank + row - 1),
                                    Word(donor.Image.Body, donorStart + 2 * (bank + row - 1)));
                        _ = SceneMidiTable.Decode(d, SceneMidiTable.SavedFields(d, PresetBody.ReadLayout(d, body).Records));
                        change = new { message_rows = rows, scope = "exact-backup-row-words-only", template_sha256 = op.TemplateSha256 };
                        break;
                    }
                    case "copy-block-settings":
                    {
                        var block = Block(Id());
                        if (op.TemplateCapture == null || op.TemplateFirmware != request.Firmware)
                            throw new ArgumentException("Block settings copy needs an explicit same-model/firmware template.");
                        byte[] template = Convert.FromBase64String(op.TemplateCapture);
                        if (template.Length > 60000 || PresetBody.Sha256(template) != op.TemplateSha256) throw new InvalidDataException("Template capture hash mismatch.");
                        var donorReport = PresetEditProjection.Decode(d, request.Firmware, template);
                        var donor = donorReport.Blocks.SingleOrDefault(b => b.EffectId == Id() && b.Record != null && b.Cells.Length == 1);
                        if (donor?.Record == null || donor.Record.Columns != block.Record!.Columns || donor.Record.Rows != block.Record.Rows ||
                            donor.Record.PrefixOffset < 0x104 + d.GridRows * d.GridColumns * 4)
                            throw new InvalidDataException("Block settings copy requires matching exact instance and record shape.");
                        Convert.FromHexString(donorReport.BodyHex).AsSpan(donor.Record.PrefixOffset, donor.Record.ByteLength)
                            .CopyTo(body.AsSpan(block.Record.PrefixOffset, block.Record.ByteLength));
                        change = new { effect_id = Id(), template_sha256 = op.TemplateSha256, copied_bytes = block.Record.ByteLength,
                            scope = "all-instance-settings-including-scene-assignments; grid-and-shared-modifiers-preserved" };
                        break;
                    }
                    case "copy-channel":
                    {
                        var block = Block(Id()); int targetChannel = Channel();
                        if (block.Record!.Rows != 4 || op.TemplateCapture == null || op.TemplateFirmware != request.Firmware ||
                            op.TemplateChannel is not ("A" or "B" or "C" or "D")) throw new ArgumentException("Channel copy requires four-channel blocks and a same-model/firmware template channel.");
                        byte[] template = Convert.FromBase64String(op.TemplateCapture);
                        if (template.Length > 60000 || PresetBody.Sha256(template) != op.TemplateSha256) throw new InvalidDataException("Template capture hash mismatch.");
                        var donorReport = PresetEditProjection.Decode(d, request.Firmware, template);
                        var donor = donorReport.Blocks.SingleOrDefault(b => b.EffectId == Id() && b.Record != null && b.Cells.Length == 1);
                        if (donor?.Record == null || donor.Record.Rows != 4 || donor.Record.Columns != block.Record.Columns)
                            throw new InvalidDataException("Channel copy needs the same exact instance and record shape.");
                        // The true instance prefix begins 12 bytes before the shape header. Channel data starts at shape+34;
                        // four complete rows end at the exact record interval boundary, excluding the following prefix.
                        int width = block.Record.Columns * 2, offset = block.Record.ShapeOffset + 34 + targetChannel * width;
                        int source = donor.Record.ShapeOffset + 34 + (op.TemplateChannel[0] - 'A') * width;
                        Convert.FromHexString(donorReport.BodyHex).AsSpan(source, width).CopyTo(body.AsSpan(offset, width));
                        change = new { effect_id = Id(), channel = op.Channel, template_channel = op.TemplateChannel,
                            template_sha256 = op.TemplateSha256, copied_bytes = width, policy = "complete-explicit-channel-settings",
                            affected_scenes = Enumerable.Range(1, 8).Where(s => Word(body, block.Record.ShapeOffset + (s - 2) * 2) == targetChannel).ToArray() };
                        break;
                    }
                    case "swap-channels":
                    {
                        var block = Block(Id()); var record = block.Record!;
                        int first = Channel();
                        if (record.Rows != 4 || op.OtherChannel is not ("A" or "B" or "C" or "D") || op.OtherChannel == op.Channel ||
                            op.Policy is not ("preserve-scene-references" or "follow-channel-settings"))
                            throw new ArgumentException("Swap requires two distinct channels on a four-channel instance and an explicit scene-reference policy.");
                        int second = op.OtherChannel[0] - 'A', width = record.Columns * 2;
                        var originalBlock = initial.Blocks.Single(b => b.EffectId == Id() && b.Record != null);
                        int sourceStart = originalBlock.Record!.ShapeOffset + 34;
                        byte[] firstRow = original.Image.Body.AsSpan(sourceStart + first * width, width).ToArray();
                        byte[] secondRow = original.Image.Body.AsSpan(sourceStart + second * width, width).ToArray();
                        var references = Enumerable.Range(1, 8).Select(s => new { scene = s,
                            channel = Word(body, record.ShapeOffset + (s - 2) * 2) }).ToArray();
                        if (references.Any(s => s.channel > 3)) throw new InvalidDataException("Swap scene references contain unsupported channel values.");
                        bool different = !firstRow.SequenceEqual(secondRow);
                        if (different)
                        {
                            secondRow.CopyTo(body, record.ShapeOffset + 34 + first * width);
                            firstRow.CopyTo(body, record.ShapeOffset + 34 + second * width);
                            if (op.Policy == "follow-channel-settings") foreach (var reference in references)
                                if (reference.channel == first || reference.channel == second)
                                    Put(body, record.ShapeOffset + (reference.scene - 2) * 2, reference.channel == first ? second : first);
                        }
                        change = new { effect_id = Id(), channel = op.Channel, other_channel = op.OtherChannel,
                            policy = op.Policy, bytes_per_channel = width, identical_rows = !different,
                            original_channel_sha256 = PresetBody.Sha256(firstRow), original_other_channel_sha256 = PresetBody.Sha256(secondRow),
                            affected_scenes = references.Where(s => s.channel == first || s.channel == second).Select(s => s.scene).ToArray(),
                            scene_references = references.Select(s => new { s.scene, before = ((char)('A' + s.channel)).ToString(),
                                after = ((char)('A' + (different && op.Policy == "follow-channel-settings" && (s.channel == first || s.channel == second)
                                    ? s.channel == first ? second : first : s.channel))).ToString() }).ToArray(),
                            scope = "two-complete-channel-rows; optional-scene-channel-references; shared-modifiers-and-other-data-preserved",
                            replay = "non-idempotent-intent; baseline-bound; identical-rows-do-not-remap-scenes; sound-equivalence-not-inferred" };
                        break;
                    }
                    case "rename-scene":
                    {
                        if (op.Scene is null or < 1 or > 8 || op.Name == null || op.Name.Length > 31 || op.Name.Any(c => c is < ' ' or > '~'))
                            throw new ArgumentException("Scene names require a scene 1–8 and at most 31 printable ASCII characters.");
                        var name = before.SceneNames[op.Scene.Value - 1];
                        if (name.Text != op.Name)
                        { body.AsSpan(name.Offset, 32).Clear(); Encoding.ASCII.GetBytes(op.Name).CopyTo(body, name.Offset); }
                        change = new { before = name.Text, after = op.Name }; break;
                    }
                    case "set-parameter":
                    case "set-block-type":
                    {
                        var block = Block(Id()); int channel = Channel();
                        var field = block.Parameters.SingleOrDefault(p => p.Key == op.Key && p.Channel == op.Channel)
                            ?? throw new InvalidDataException("No saved field mapping for this model, block shape and semantic key.");
                        if (op.Value == null || !double.IsFinite(op.Value.Value) || op.Unit != field.Unit)
                            throw new ArgumentException("Use a finite value and the field's declared unit.");
                        if (field.Status != "decoded-candidate") throw new InvalidDataException("Field is not applicable to the selected channel mode.");
                        bool numericId = field.Scale == "identity";
                        if (field.Scale is not ("identity" or "u16-to-0-10"))
                            throw new ArgumentException("Use set-scaled-parameter with physical_value for this built-in scale.");
                        if (numericId && op.Policy != "preserve-other-fields")
                            throw new ArgumentException("Numeric selections require policy preserve-other-fields; no implicit type reset/defaults.");
                        if (op.Kind == "set-block-type" && !op.Key!.EndsWith(".type", StringComparison.Ordinal)) throw new ArgumentException("set-block-type requires a type key.");
                        double requested = op.Value.Value;
                        if (requested < 0 || requested > (numericId ? 65535 : 10) || numericId && requested != Math.Truncate(requested))
                            throw new ArgumentException("Value exceeds the saved field range or integer identifier domain.");
                        int raw = numericId ? (int)requested : (int)Math.Round(requested * 65535 / 10, MidpointRounding.AwayFromZero);
                        Put(body, field.BodyOffset, raw);
                        int[] scenes = Enumerable.Range(1, 8).Where(s => Word(body, block.Record!.ShapeOffset + (s - 2) * 2) == channel).ToArray();
                        change = new { key = field.Key, channel = op.Channel, before = field.Value, requested,
                            effective_saved_value = numericId ? raw : raw * 10.0 / 65535, raw_before = field.RawValue, raw_after = raw,
                            unit = field.Unit, affected_scenes = scenes, policy = op.Policy, effective_live_value = "controller-dependent-not-inferred" }; break;
                    }
                    case "set-scene-block-state":
                    {
                        var block = Block(Id()); var record = block.Record!;
                        if (record.Rows != 4 || op.Scene is null or < 1 or > 8 || op.Channel == null && op.Bypassed == null)
                            throw new ArgumentException("Scene assignment requires a four-channel block, scene 1–8 and channel and/or bypassed.");
                        int channelOffset = record.ShapeOffset + (op.Scene.Value - 2) * 2;
                        int bypassOffset = record.ShapeOffset + (op.Scene.Value + 6) * 2;
                        int oldChannel = Word(body, channelOffset), oldBypass = Word(body, bypassOffset);
                        if (oldChannel > 3 || oldBypass > 1) throw new InvalidDataException("Scene assignment contains unsupported state/ignore values.");
                        if (op.Channel != null) Put(body, channelOffset, Channel());
                        if (op.Bypassed != null) Put(body, bypassOffset, op.Bypassed.Value ? 1 : 0);
                        change = new { scene = op.Scene, before_channel = oldChannel, after_channel = Word(body, channelOffset),
                            before_bypassed = oldBypass != 0, after_bypassed = Word(body, bypassOffset) != 0 }; break;
                    }
                    case "connect": case "disconnect":
                    {
                        int source = Cell(op.SourceRow, op.SourceColumn), dest = Cell(op.DestinationRow, op.DestinationColumn);
                        if (op.DestinationColumn != op.SourceColumn + 1) throw new ArgumentException("Grid edges join adjacent columns in the forward direction.");
                        if (Word(body, source) == 0 || Word(body, dest) == 0) throw new InvalidDataException("Routing endpoints must be occupied.");
                        KnownRoute(dest); int mask = 1 << (op.SourceRow!.Value - 1), old = Word(body, dest + 2);
                        Put(body, dest + 2, op.Kind == "connect" ? old | mask : old & ~mask);
                        change = new { before_mask = old, after_mask = Word(body, dest + 2) }; break;
                    }
                    case "insert-shunt":
                    {
                        int cell = Cell(op.Row, op.Column), id = Id(), old = Word(body, cell);
                        if (id is < 1024 or > 1029) throw new ArgumentException("Supply an explicit saved shunt identity 1024–1029.");
                        if (old != 0 && old != id) throw new InvalidDataException("Chosen shunt cell is occupied.");
                        KnownRoute(cell); Put(body, cell, id); break;
                    }
                    case "remove-shunt":
                    {
                        int cell = Cell(op.Row, op.Column), old = Word(body, cell);
                        if (op.Policy != "disconnect") throw new ArgumentException("Shunt removal requires explicit disconnect policy and explicit replacement connections.");
                        if (old == 0) break;
                        if (old is < 1024 or > 1029) throw new InvalidDataException("Selected cell is not a shunt.");
                        DisconnectCell(op.Row!.Value, op.Column!.Value); Put(body, cell, 0); break;
                    }
                    case "remove-block":
                    {
                        if (op.Policy is not ("disconnect" or "reconnect" or "replace-with-shunt")) throw new ArgumentException("Choose disconnect, reconnect or replace-with-shunt.");
                        if (op.Policy == "reconnect" && !operations.Any(o => o.Kind == "connect")) throw new ArgumentException("Reconnect requires explicit replacement connect operations.");
                        if (before.Blocks.All(b => b.EffectId != Id())) break;
                        var block = Block(Id()); var cell = block.Cells[0]; var record = block.Record!;
                        if (record.Columns == 25 && record.Rows == 1) throw new InvalidDataException("System records are not removable block templates.");
                        if (op.Policy != "replace-with-shunt") DisconnectCell(cell.Row, cell.Column);
                        else KnownRoute(Cell(cell.Row, cell.Column));
                        // Shunt identity is explicit in value; never infer a device default.
                        int replacement = op.Policy == "replace-with-shunt" ? op.Value is >= 1024 and <= 1029 && op.Value == Math.Truncate(op.Value.Value)
                            ? (int)op.Value : throw new ArgumentException("replace-with-shunt requires value 1024–1029.") : 0;
                        Put(body, Cell(cell.Row, cell.Column), replacement);
                        body = body.Take(record.PrefixOffset).Concat(body.Skip(record.PrefixOffset + record.ByteLength)).ToArray();
                        change = new { removed_effect_id = Id(), row = cell.Row, column = cell.Column, policy = op.Policy,
                            processing_removed = true, replacement_effect_id = replacement }; break;
                    }
                    case "insert-block":
                    {
                        int id = Id(), offset = Cell(op.Row, op.Column);
                        var existing = before.Blocks.Where(b => b.EffectId == id).ToArray();
                        if (existing.Length > 0)
                        {
                            if (existing.Length != 1 || existing[0].Cells.Length != 1 || existing[0].Cells[0].Row != op.Row || existing[0].Cells[0].Column != op.Column)
                                throw new InvalidDataException("Instance already exists at a different or ambiguous cell.");
                            break;
                        }
                        if (Word(body, offset) is not (0 or >= 1024 and <= 1029)) throw new InvalidDataException("Insertion cell is occupied by another block.");
                        if (Word(body, offset) != 0 && op.Policy != "replace-shunt") throw new ArgumentException("Replacing a shunt requires explicit replace-shunt policy.");
                        KnownRoute(offset);
                        if (op.TemplateCapture == null || op.TemplateFirmware != request.Firmware) throw new ArgumentException("Insertion needs an explicit same-model/firmware template capture.");
                        byte[] template = Convert.FromBase64String(op.TemplateCapture);
                        if (template.Length > 60000 || PresetBody.Sha256(template) != op.TemplateSha256) throw new InvalidDataException("Template capture hash mismatch.");
                        var templateRead = PresetEditProjection.Decode(d, request.Firmware, template);
                        var donor = templateRead.Blocks.SingleOrDefault(b => b.EffectId == id && b.Record != null && b.Cells.Length == 1)
                            ?? throw new InvalidDataException("Template lacks the exact uniquely identified instance.");
                        var record = donor.Record!;
                        if (record.PrefixOffset < 0x104 + d.GridRows * d.GridColumns * 4) throw new InvalidDataException("Template instance overlaps the routing grid.");
                        if (id is < 1 or > 1000 || record.Columns == 25 && record.Rows == 1) throw new InvalidDataException("Template is not an insertable effect record.");
                        var identity = BlockCatalogue.Resolve(d.Model, id);
                        if (identity.Availability.DocumentedAvailable == false) throw new InvalidDataException("Instance exceeds the model's documented instance limit.");
                        int start = before.Records.Where(r => !(r.Columns == 25 && r.Rows == 1)).Select(r => r.PrefixOffset).DefaultIfEmpty(-1).Min();
                        if (start < 0)
                        {
                            var last = before.Records.LastOrDefault() ?? throw new InvalidDataException("No structural record-chain anchor.");
                            start = last.PrefixOffset + last.ByteLength;
                        }
                        if (start < 0x104 + d.GridRows * d.GridColumns * 4) throw new InvalidDataException("Instance interval overlaps the routing grid.");
                        var donorBody = Convert.FromHexString(templateRead.BodyHex);
                        byte[] interval = donorBody.AsSpan(record.PrefixOffset, record.ByteLength).ToArray();
                        body = body.Take(start).Concat(interval).Concat(body.Skip(start)).ToArray(); Put(body, offset, id);
                        change = new { effect_id = id, row = op.Row, column = op.Column, initialization = "complete-explicit-template-instance",
                            template_sha256 = op.TemplateSha256, template_body_sha256 = templateRead.BodySha256 }; break;
                    }
                    default: throw new ArgumentException("Unknown edit operation.");
                }
                items.Add(new(op, previous.SequenceEqual(body) ? "unchanged" : "planned", "resolved", "Resolved against retained complete bytes.", change));
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or FormatException or InvalidOperationException)
            { body = previous; items.Add(new(op, "conflict", "invalid-operation", e.Message)); }
        }
        try
        {
            var final = Projection();
            var beforeIssues = initial.Issues.Select(i => SnapshotExport.Json(i)).ToHashSet(StringComparer.Ordinal);
            var newIssues = final.Issues.Where(i => !beforeIssues.Contains(SnapshotExport.Json(i))).ToArray();
            if (newIssues.Length != 0) throw new InvalidDataException("Final structure introduces unresolved fields/edges: " + string.Join(", ", newIssues.Select(i => i.Code)));
            var oldSpecial = initial.SpecialRouting!; var newSpecial = final.SpecialRouting!;
            // Preserve existing feedback without a blanket gate. Creating or rewiring a loop needs explicit intent.
            bool graphChanged = SnapshotExport.Json(initial.Routing.Edges ?? []) != SnapshotExport.Json(final.Routing.Edges ?? []) ||
                SnapshotExport.Json(oldSpecial.Links) != SnapshotExport.Json(newSpecial.Links);
            if (graphChanged && request.FeedbackPolicy != "allow")
            {
                var beforeEdges = (initial.Routing.Edges ?? []).Select(e => (e.Source, e.Destination))
                    .Concat(oldSpecial.Links.Select(e => (e.Source, e.Destination))).ToHashSet();
                var afterEdges = (final.Routing.Edges ?? []).Select(e => (e.Source, e.Destination))
                    .Concat(newSpecial.Links.Select(e => (e.Source, e.Destination)));
                if (afterEdges.Any(e => !beforeEdges.Contains(e) && newSpecial.FeedbackComponents.Any(c => c.Contains(e.Source) && c.Contains(e.Destination))))
                    throw new InvalidDataException("The final graph adds a feedback-loop edge. Set feedback_policy to allow to express this routing intent; no Return level is changed automatically.");
            }
            if (Word(body, 0) + 1 != scene) throw new InvalidDataException("Saved default scene changed.");
        }
        catch (InvalidDataException e) { items.Add(new(new("validate-final"), "conflict", "invalid-final-state", e.Message)); }
        byte[] expected = items.Any(i => i.Status == "conflict") ? baseline : PresetBodyWriter.ReplaceBody(d, baseline, body);
        return new(d, request, baseline, expected, items.ToArray(), scene);
    }
    private static PresetEditPlan CreateOrganizationImage(DeviceDefinition device, PresetEditRequest request, byte[] baseline)
    {
        if (request.Operations.Length != 1) throw new ArgumentException("Whole-preset organization cannot compose with parameter or grid edits.");
        var op = request.Operations[0]; ValidateFields(op);
        byte[] expected;
        if (op.Kind == "rename-preset")
        { if (op.Name == null) throw new ArgumentException("Explicit preset name required."); expected = PresetBodyWriter.RenamePreset(device, baseline, op.Name); }
        else
        {
            if (op.TemplateCapture == null || op.TemplateSha256 == null || op.TemplateFirmware != request.Firmware)
                throw new ArgumentException("Complete same-model/firmware donor required.");
            byte[] donor = Convert.FromBase64String(op.TemplateCapture);
            if (PresetBody.Sha256(donor) != op.TemplateSha256) throw new InvalidDataException("Whole-preset donor hash differs.");
            expected = PresetFile.AddressSaved(device, donor, request.Slot);
            if (op.Name != null) expected = PresetBodyWriter.RenamePreset(device, expected, op.Name);
        }
        var before = SnapshotExport.DecodeFile(device, baseline).Image;
        var after = SnapshotExport.DecodeFile(device, expected).Image;
        int scene = Word(after.Body, 0) + 1;
        if (scene is < 1 or > 8) throw new InvalidDataException("Donor saved default scene outside 1–8.");
        // Identical content with a different qualified compression representation needs no Save.
        if (PresetUploadPlan.EquivalentCompression(before.RawImage, before.Body, after.RawImage, after.Body)) expected = baseline.ToArray();
        bool changed = !baseline.SequenceEqual(expected);
        return new(device, request, baseline, expected,
            [new(op, changed ? "planned" : "unchanged", "resolved", "Complete image; only explicit rename changes donor content.",
                new { before_name = before.Name, after_name = after.Name, before_saved_scene = Word(before.Body, 0) + 1,
                    after_saved_scene = scene, donor_sha256 = op.TemplateSha256, unrelated_references_rewritten = false })], scene);
    }
    private static int Phase(PresetEditOperation o) => o.Kind switch
    { "remove-block" or "remove-shunt" => 0, "insert-block" or "insert-shunt" => 1, "copy-block-settings" or "copy-channel" or "swap-channels" => 2, "set-block-type" => 3,
        "set-parameter" when o.Key == "cab.mode" => 3, "connect" or "disconnect" => 5, _ => 4 };
    private static string Target(PresetEditOperation o) => o.Kind switch
    {
        "rename-scene" => $"name:{o.Scene}",
        "set-scene-midi" => $"midi:{o.Scene}:{o.MessageSlot}",
        "restore-scene-midi-rows" => "midi:receipt-owned-rows",
        "set-parameter" or "set-block-type" or "set-calibrated-parameter" or "set-scene-ignore" or "set-scaled-parameter" => $"parameter:{o.EffectId}:{o.Channel}:{o.Key}",
        "set-modifier-parameter" or "set-calibrated-modifier" or "copy-modifier-selector" => $"modifier:{o.ModifierSlot}:{o.Key}",
        "set-modifier-source" or "copy-modifier" => $"modifier:{o.ModifierSlot}",
        "set-scene-block-state" => $"state:{o.EffectId}:{o.Scene}",
        "copy-channel" => $"channel:{o.EffectId}:{o.Channel}",
        "swap-channels" => $"swap:{o.EffectId}",
        "copy-block-settings" => $"settings:{o.EffectId}",
        "connect" or "disconnect" => $"edge:{o.SourceRow}:{o.SourceColumn}:{o.DestinationRow}:{o.DestinationColumn}",
        "insert-block" or "remove-block" => $"block:{o.EffectId}",
        _ => $"cell:{o.Row}:{o.Column}"
    };
    private static void ValidateFields(PresetEditOperation o)
    {
        string[] permitted = o.Kind switch
        {
            "rename-scene" => ["scene", "name"],
            "rename-preset" => ["name"],
            "copy-preset" => ["template_capture", "template_sha256", "template_firmware", "name"],
            "set-calibrated-parameter" => ["effect_id", "channel", "key", "calibrated_value", "unit", "calibration"],
            "set-scaled-parameter" => ["effect_id", "channel", "key", "physical_value", "unit"],
            "set-modifier-parameter" => ["modifier_slot", "expected_binding", "key", "physical_value", "unit"],
            "set-calibrated-modifier" => ["modifier_slot", "expected_binding", "key", "calibrated_value", "unit", "calibration"],
            "copy-modifier-selector" => ["modifier_slot", "expected_binding", "key", "template_modifier_slot", "template_capture", "template_sha256", "template_firmware"],
            "set-scene-ignore" => ["effect_id", "key", "enabled", "unit", "calibration"],
            "set-modifier-source" => ["modifier_slot", "source_id", "expected_binding"],
            "copy-modifier" => ["modifier_slot", "template_modifier_slot", "expected_binding", "template_capture", "template_sha256", "template_firmware"],
            "set-scene-midi" => ["scene", "message_slot", "message_type", "midi_channel", "controller", "value", "policy", "expected_raw"],
            "restore-scene-midi-rows" => ["message_rows", "expected_rows_raw", "template_capture", "template_sha256", "template_firmware"],
            "set-parameter" or "set-block-type" => ["effect_id", "channel", "key", "value", "unit", "policy"],
            "set-scene-block-state" => ["effect_id", "scene", "channel", "bypassed"],
            "copy-channel" => ["effect_id", "channel", "template_capture", "template_sha256", "template_firmware", "template_channel"],
            "swap-channels" => ["effect_id", "channel", "other_channel", "policy"],
            "copy-block-settings" => ["effect_id", "template_capture", "template_sha256", "template_firmware"],
            "connect" or "disconnect" => ["source_row", "source_column", "destination_row", "destination_column"],
            "insert-block" => ["effect_id", "row", "column", "policy", "template_capture", "template_sha256", "template_firmware"],
            "remove-block" => ["effect_id", "policy", "value"],
            "insert-shunt" => ["effect_id", "row", "column"],
            "remove-shunt" => ["row", "column", "policy"],
            _ => throw new ArgumentException("Unknown operation kind.")
        };
        foreach (var p in JsonSerializer.SerializeToElement(o, RequestJson).EnumerateObject())
            if (p.Name != "kind" && p.Value.ValueKind != JsonValueKind.Null && !permitted.Contains(p.Name))
                throw new ArgumentException("Unexpected operation field: " + p.Name);
    }
    private static int Word(byte[] body, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset, 2));
    private static void Put(byte[] body, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset, 2), checked((ushort)value));
    public void WriteBundle(string destination)
    {
        if (!Executable) throw new InvalidOperationException("Resolve plan conflicts before creating an executable bundle.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new edit bundle directory.");
        Directory.CreateDirectory(destination);
        File.WriteAllBytes(Path.Combine(destination, "baseline.syx"), baseline);
        File.WriteAllBytes(Path.Combine(destination, "expected.syx"), expected);
        File.WriteAllText(Path.Combine(destination, "request.json"), JsonSerializer.Serialize(request, RequestJson));
        File.WriteAllText(Path.Combine(destination, "plan.json"), SnapshotExport.Json(Report()));
    }
    public static PresetEditPlan OpenBundle(string directory, string expectedPlanHash, bool allowPriorReceiptRelease = false)
    {
        byte[] Read(string name, int max)
        {
            string path = Path.Combine(directory, name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > max)
                throw new InvalidDataException("Invalid edit bundle file.");
            return File.ReadAllBytes(path);
        }
        var plan = Create(Parse(Read("request.json", 4_000_000)), Read("baseline.syx", 60000));
        byte[] recordedReport = Read("plan.json", 4_000_000);
        bool reportMatches = plan.PlanSha256 == expectedPlanHash && PresetBody.Sha256(recordedReport) == plan.PlanSha256;
        if (!reportMatches && allowPriorReceiptRelease && PresetBody.Sha256(recordedReport) == expectedPlanHash)
        {
            using var oldDocument = JsonDocument.Parse(recordedReport);
            using var newDocument = JsonDocument.Parse(SnapshotExport.Json(plan.Report()));
            var oldRoot = oldDocument.RootElement; var newRoot = newDocument.RootElement;
            reportMatches = oldRoot.ValueKind == JsonValueKind.Object && newRoot.ValueKind == JsonValueKind.Object &&
                oldRoot.GetProperty("protocol_release").GetString() is "0.25.0" or "0.26.0" or "0.27.0" or "0.28.0" or "0.29.0" or "0.30.0" or "0.31.0" or "0.32.0" or "0.33.0" or "0.34.0" or "0.35.0" or "0.36.0" &&
                oldRoot.EnumerateObject().Count() == newRoot.EnumerateObject().Count() &&
                oldRoot.EnumerateObject().All(property => property.Name == "protocol_release" ||
                    newRoot.TryGetProperty(property.Name, out var currentValue) && JsonElement.DeepEquals(property.Value, currentValue));
        }
        if (!plan.Executable || !reportMatches ||
            !plan.expected.SequenceEqual(Read("expected.syx", 60000))) throw new InvalidDataException("Edit bundle differs from its freshly resolved plan.");
        return plan;
    }
}
