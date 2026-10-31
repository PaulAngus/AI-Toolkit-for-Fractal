using System.Buffers.Binary;
using System.Text;

namespace Fractal.Core;

public sealed record EditReadIssue(string Code, string Detail, int? EffectId = null);
public sealed record SavedNameRead(int Scene, int Offset, string RawHex, string? Text, string Status);
public sealed record SavedParameterRead(string Key, string Channel, int BodyOffset, int RawValue, string Unit,
    double? Value = null, string Status = "decoded-candidate", string Scale = "identity");
public sealed record SavedSceneStateRead(int Scene, int ChannelOffset, int BypassOffset, int RawChannel,
    int RawBypass, string? Channel, bool? Bypassed, string Status);
public sealed record InstanceRecordRead(int ShapeOffset, int PrefixOffset, int ByteLength, int EffectId, int Columns, int Rows);
public sealed record SavedBlockRead(int EffectId, BlockIdentity Identity, GridCell[] Cells, string IdentityStatus,
    InstanceRecordRead? Record, string ParameterStatus, SavedParameterRead[] Parameters,
    string SceneStateStatus, SavedSceneStateRead[] Scenes);
public sealed record RoutingWordRead(int Row, int Column, int EffectId, int BodyOffset, int RawWord,
    int KnownMask, int UnknownBits);
public sealed record RoutingNodeRead(string Key, int Row, int Column, int EffectId, string Kind);
public sealed record RoutingEdgeRead(string Source, string Destination);
public sealed record RoutingRead(string Status, string Encoding, RoutingNodeRead[] Nodes, RoutingEdgeRead[]? Edges,
    RoutingWordRead[] Words, string RawGridHex, bool HardwareQualified);
public sealed record PresetEditReadReport(int SchemaVersion, string ProtocolRelease, EditReadScope Scope,
    int Slot, string SysexSha256, string RawSha256, string BodySha256, string BodyHex,
    SavedNameRead[] SceneNames, SavedBlockRead[] Blocks, InstanceRecordRead[] Records,
    RoutingRead Routing, EditReadIssue[] Issues, string Status, bool PortsOpened, bool ExecutionSupported,
    SpecialRoutingReport? SpecialRouting = null, SavedModifier[]? Modifiers = null, BuiltinScaleReport? BuiltinScales = null);

/// <summary>Offline semantic projection from a complete integrity-checked saved capture.
/// Does not manufacture edit authority, guess unknown fields, or modify the source image.</summary>
public static class PresetEditProjection
{
    public static PresetEditReadReport Decode(DeviceDefinition device, string firmware, byte[] savedWire)
    {
        var profile = EditReadCatalogue.Select(device, firmware);
        // Validate at the public boundary, even if a caller previously decoded this capture.
        var wire = savedWire.ToArray();
        var saved = SnapshotExport.DecodeFile(device, wire);
        byte[] body = saved.Image.Body;
        List<EditReadIssue> issues = [];
        var names = ReadNames(body, profile, issues);
        PresetBodyLayout? layout = null;
        try { layout = PresetBody.ReadLayout(device, body); }
        catch (InvalidDataException error) { issues.Add(new("unsupported-body-layout", error.Message)); }
        var records = layout == null ? [] : ReadRecords(body, layout, profile);
        var blocks = layout == null ? [] : ReadBlocks(device, body, layout, records, profile, issues);
        var routing = ReadRouting(body, layout, profile, issues);
        issues.Add(new("partial-audio-coverage", "Only catalogue saved-field descriptors and source-qualified modifier bindings are decoded. Opaque modifier curves, other settings and unresolved enum meanings remain raw; editor IDs do not establish saved offsets."));
        return new(1, ProtocolRelease.Version, profile.Scope, saved.Image.Slot, PresetBody.Sha256(wire),
            PresetBody.Sha256(saved.Image.RawImage), PresetBody.Sha256(body), Convert.ToHexStringLower(body),
            names, blocks, records, routing, issues.ToArray(), "partial", false, false, Fractal.Core.SpecialRouting.Decode(device, routing, blocks),
            layout == null ? [] : SavedModifiers.Read(device, body, firmware, blocks), Fractal.Core.BuiltinScales.Catalogue(device, firmware));
    }

    private static SavedNameRead[] ReadNames(byte[] body, SavedReadProfile profile, List<EditReadIssue> issues)
    {
        var p = profile.Data.GetProperty("scene_names");
        int start = p.GetProperty("offset").GetInt32(), length = p.GetProperty("bytes_per_name").GetInt32();
        return Enumerable.Range(1, p.GetProperty("count").GetInt32()).Select(scene =>
        {
            int offset = start + (scene - 1) * length;
            var raw = body.AsSpan(offset, length);
            int end = raw.IndexOf((byte)0); if (end < 0) end = raw.Length;
            bool printable = true;
            foreach (byte value in raw[..end]) if (value is < 32 or > 126) printable = false;
            if (!printable) issues.Add(new("unsupported-scene-name-encoding", $"Scene {scene} is not printable ASCII; raw bytes retained."));
            // Bytes after termination and trailing spaces remain in RawHex; no lossy re-encoding contract.
            return new SavedNameRead(scene, offset, Convert.ToHexStringLower(raw),
                printable ? Encoding.ASCII.GetString(raw[..end]).TrimEnd(' ') : null,
                printable ? "decoded-candidate" : "unsupported-encoding");
        }).ToArray();
    }

    private static InstanceRecordRead[] ReadRecords(byte[] body, PresetBodyLayout layout, SavedReadProfile profile) =>
        layout.Records.Select(record =>
        {
            int prefix = record.Offset + profile.PrefixDelta;
            int length = (23 + record.Columns * record.Rows) * 2;
            if (prefix < 0 || prefix + length > body.Length) throw new InvalidDataException("Instance interval outside body.");
            return new InstanceRecordRead(record.Offset, prefix, length, Word(body, prefix), record.Columns, record.Rows);
        }).ToArray();

    private static SavedBlockRead[] ReadBlocks(DeviceDefinition device, byte[] body, PresetBodyLayout layout,
        InstanceRecordRead[] records, SavedReadProfile profile, List<EditReadIssue> issues)
    {
        var amp = profile.AmpType;
        var audioFields = profile.AudioFields;
        return layout.Grid.Where(c => c.EffectId != 0 && c.Kind != "shunt").GroupBy(c => c.EffectId).OrderBy(g => g.Key).Select(group =>
        {
            int id = group.Key;
            var cells = group.ToArray();
            var identity = BlockCatalogue.Resolve(device.Model, id);
            var matches = records.Where(r => r.EffectId == id).ToArray();
            string identityStatus = cells.Length != 1 ? "duplicate-grid-instance" : matches.Length switch
            { 0 => "missing-instance-record", 1 => "exact-prefix-match", _ => "ambiguous-instance-record" };
            var record = identityStatus == "exact-prefix-match" ? matches[0] : null;
            if (record == null) issues.Add(new(identityStatus, "Requires one grid cell and one exact instance prefix; shape/order is not an identity.", id));
            string parameterStatus = "unsupported-family-profile", sceneStatus = "unsupported-family-profile";
            List<SavedParameterRead> parameters = [];
            List<SavedSceneStateRead> scenes = [];
            if (record == null) parameterStatus = sceneStatus = "unresolved-instance";
            else if (amp.EffectIds.Contains(id))
            {
                if (record.Columns != amp.Columns || record.Rows != amp.Rows)
                {
                    parameterStatus = sceneStatus = "unsupported-record-shape";
                    issues.Add(new(parameterStatus, "Instance exists, but its shape differs from the model-specific Amp profile.", id));
                }
                else
                {
                    parameterStatus = sceneStatus = "decoded-candidate";
                    for (int ch = 0; ch < amp.Channels.Length; ch++)
                    {
                        int offset = record.ShapeOffset + amp.SavedOffsets[ch];
                        int raw = Word(body, offset);
                        parameters.Add(new(amp.Key, amp.Channels[ch], offset, raw, amp.Unit, raw));
                    }
                    int[] channels = profile.ChannelOffsets, bypasses = profile.BypassOffsets;
                    for (int s = 0; s < channels.Length; s++)
                    {
                        int channelOffset = record.ShapeOffset + channels[s], bypassOffset = record.ShapeOffset + bypasses[s];
                        int channel = Word(body, channelOffset), bypass = Word(body, bypassOffset);
                        bool valid = channel < amp.Channels.Length && bypass <= 1;
                        if (!valid) sceneStatus = "partial-unsupported-state";
                        scenes.Add(new(s + 1, channelOffset, bypassOffset, channel, bypass,
                            channel < amp.Channels.Length ? amp.Channels[channel] : null,
                            bypass <= 1 ? bypass == 1 : null, valid ? "decoded-candidate" : "unsupported-state-value"));
                    }
                    if (sceneStatus != "decoded-candidate") issues.Add(new("unsupported-state-value", "Raw scene fields retained; no channel/bypass coercion.", id));
                }
            }
            if (record != null)
            {
                if (scenes.Count == 0 && record.Rows == 4)
                {
                    for (int s = 1; s <= 8; s++)
                    {
                        int channelOffset = record.ShapeOffset + (s - 2) * 2, bypassOffset = record.ShapeOffset + (s + 6) * 2;
                        int channel = Word(body, channelOffset), bypass = Word(body, bypassOffset);
                        scenes.Add(new(s, channelOffset, bypassOffset, channel, bypass,
                            channel < 4 ? ((char)('A' + channel)).ToString() : null, bypass <= 1 ? bypass == 1 : null,
                            channel < 4 && bypass <= 1 ? "decoded-candidate" : "unsupported-state-value"));
                    }
                    sceneStatus = scenes.All(s => s.Status == "decoded-candidate") ? "decoded-candidate" : "partial-unsupported-state";
                }
                var candidates = audioFields.Where(f => f.EffectIds.Contains(id)).ToArray();
                var fields = candidates.Where(f => f.Columns == record.Columns && f.Rows == record.Rows).ToArray();
                if (candidates.Length > 0 && fields.Length == 0)
                {
                    parameterStatus = "unsupported-record-shape";
                    issues.Add(new(parameterStatus, "No saved audio descriptor matches this instance's model-specific record shape.", id));
                }
                foreach (var field in fields)
                {
                    for (int ch = 0; ch < field.Channels.Length; ch++)
                    {
                        int offset = record.ShapeOffset + field.SavedOffsets[ch];
                        int raw = Word(body, offset);
                        bool applicable = field.Condition == null || Word(body, record.ShapeOffset +
                            fields.Single(f => f.Key == field.Condition.Key).SavedOffsets[ch]) == field.Condition.EqualsRaw;
                        double? value = applicable ? field.Scale == "identity" ? raw : raw * 10.0 / 65535 : null;
                        parameters.Add(new(field.Key, field.Channels[ch], offset, raw, field.Unit, value,
                            applicable ? "decoded-candidate" : "not-applicable", field.Scale));
                    }
                    parameterStatus = "decoded-candidate";
                }
                foreach (var field in BuiltinScales.Catalogue(device, profile.Scope.Firmware).Fields.Where(f => f.EffectIds.Contains(id) && f.Columns == record.Columns && record.Rows == 4))
                {
                    for (int ch = 0; ch < 4; ch++)
                    {
                        int offset = record.ShapeOffset + 34 + 2 * (ch * record.Columns + field.ParameterId), raw = Word(body, offset);
                        parameters.Add(new(field.Key, ((char)('A' + ch)).ToString(), offset, raw, field.Scale.Unit,
                            field.Scale.Decode(raw), raw <= field.Scale.RawMaximum ? "decoded-candidate" : "unknown-raw-sentinel", "builtin-" + field.Scale.Taper));
                    }
                    parameterStatus = "decoded-candidate";
                }
            }
            return new SavedBlockRead(id, identity, cells, identityStatus, record, parameterStatus,
                parameters.ToArray(), sceneStatus, scenes.ToArray());
        }).ToArray();
    }

    private static RoutingRead ReadRouting(byte[] body, PresetBodyLayout? layout, SavedReadProfile profile, List<EditReadIssue> issues)
    {
        if (layout == null) return new("unsupported-body-layout", profile.RoutingEncoding, [], null, [], "", false);
        int mask = profile.RoutingMask;
        var words = layout.Grid.Select((c, index) => new RoutingWordRead(c.Row, c.Column, c.EffectId,
            0x104 + index * 4 + 2, c.RouteFlag, mask, c.RouteFlag & ~mask)).ToArray();
        var nodes = layout.Grid.Where(c => c.EffectId != 0).Select(c => new RoutingNodeRead(
            $"cell:{c.Row}:{c.Column}", c.Row, c.Column, c.EffectId, c.Kind)).ToArray();
        var cells = layout.Grid.ToDictionary(c => (c.Row, c.Column));
        List<RoutingEdgeRead> edges = [];
        bool partial = false;
        foreach (var word in words)
        {
            void Issue(string code, string detail)
            {
                partial = true;
                issues.Add(new(code, $"Cell {word.Row},{word.Column}: {detail}", word.EffectId));
            }
            if (word.UnknownBits != 0) Issue("unknown-routing-bits", $"Uninterpreted bits 0x{word.UnknownBits:x4} retained.");
            int incoming = word.RawWord & mask;
            if (incoming == 0) continue;
            if (word.Column == 1)
            {
                Issue("unresolved-routing-boundary", "Incoming mask has no preceding grid column; no external endpoint inferred.");
                continue;
            }
            if (word.EffectId == 0)
            {
                Issue("routing-empty-destination", "Incoming mask targets an empty cell; no edge inferred.");
                continue;
            }
            for (int row = 1; (1 << (row - 1)) <= mask; row++)
            {
                if ((incoming & (1 << (row - 1))) == 0) continue;
                var source = cells[(row, word.Column - 1)];
                if (source.EffectId == 0)
                {
                    Issue("routing-empty-source", $"Source row {row} in the preceding column is empty; no edge inferred.");
                    continue;
                }
                edges.Add(new($"cell:{row}:{word.Column - 1}", $"cell:{word.Row}:{word.Column}"));
            }
        }
        return new(partial ? "partial" : "decoded-candidate", profile.RoutingEncoding, nodes, edges.ToArray(), words,
            Convert.ToHexStringLower(body.AsSpan(0x104, layout.Grid.Length * 4)), false);
    }
    private static int Word(byte[] body, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset, 2));
}
