namespace Fractal.Core;

public sealed record ScenePermutationResult(string Status, int[]? BeforeToAfter, string Scope);

/// <summary>Offline, byte-exact proof for permutations of the scene fields we can locate.
/// An unmodelled changed byte prevents a positive result.</summary>
public static class ScenePermutationAnalysis
{
    private sealed record Span(int Offset, int Length);
    private const string Scope = "Scene names, placed block channel/bypass words and complete Scene MIDI message banks; every other decoded body byte must remain identical. This is saved-byte correspondence, not audible or physical scene identity.";

    public static ScenePermutationResult Compare(DeviceDefinition device, string firmware, byte[] beforeWire, byte[] afterWire)
    {
        _ = SnapshotExport.DecodeFile(device, beforeWire);
        _ = SnapshotExport.DecodeFile(device, afterWire);
        PresetEditReadReport before, after;
        try
        {
            before = PresetEditProjection.Decode(device, firmware, beforeWire);
            after = PresetEditProjection.Decode(device, firmware, afterWire);
        }
        catch (InvalidDataException) { return new("incomplete-scene-layout", null, Scope); }
        byte[] a = Convert.FromHexString(before.BodyHex), b = Convert.FromHexString(after.BodyHex);
        ScenePermutationResult Unknown(string status) => new(status, null, Scope);
        if (a.Length != b.Length || before.Slot != after.Slot) return Unknown("incompatible-captures");
        if (a.AsSpan().SequenceEqual(b)) return Unknown("unchanged-body");
        if (before.Routing.Status != "decoded-candidate" || after.Routing.Status != "decoded-candidate" ||
            before.Blocks.Length != after.Blocks.Length || before.SceneNames.Length != 8 || after.SceneNames.Length != 8)
            return Unknown("incomplete-scene-layout");

        var scenes = Enumerable.Range(0, 8).Select(_ => new List<Span>()).ToArray();
        for (int s = 0; s < 8; s++)
        {
            var nameA = before.SceneNames[s]; var nameB = after.SceneNames[s];
            int length = nameA.RawHex.Length / 2;
            if (nameA.Offset != nameB.Offset || length == 0 || nameA.RawHex.Length != nameB.RawHex.Length ||
                nameA.Offset < 0 || nameA.Offset + length > a.Length)
                return Unknown("incomplete-scene-layout");
            scenes[s].Add(new(nameA.Offset, length));
        }
        foreach (var (left, right) in before.Blocks.Zip(after.Blocks))
        {
            if (left.EffectId != right.EffectId || left.Record?.ShapeOffset != right.Record?.ShapeOffset ||
                left.Record?.ByteLength != right.Record?.ByteLength || left.Record?.Columns != right.Record?.Columns ||
                left.Record?.Rows != right.Record?.Rows ||
                !left.Cells.SequenceEqual(right.Cells)) return Unknown("changed-block-layout");
            if (left.Record == null || right.Record == null || left.SceneStateStatus != "decoded-candidate" ||
                right.SceneStateStatus != "decoded-candidate" || left.Scenes.Length != 8 || right.Scenes.Length != 8)
            {
                // Scene MIDI has its own complete four-bank layout below.
                if (left.EffectId == 190 && left.Record != null && right.Record != null) continue;
                return Unknown("incomplete-scene-layout");
            }
            for (int s = 0; s < 8; s++)
            {
                var x = left.Scenes[s]; var y = right.Scenes[s];
                if (x.ChannelOffset != y.ChannelOffset || x.BypassOffset != y.BypassOffset)
                    return Unknown("changed-block-layout");
                scenes[s].Add(new(x.ChannelOffset, 2));
                scenes[s].Add(new(x.BypassOffset, 2));
            }
        }
        if (before.Blocks.Count(x => x.EffectId == 190) > 1) return Unknown("incomplete-scene-midi-layout");
        if (before.Blocks.Any(x => x.EffectId == 190))
        {
            var midi = before.Blocks.Single(x => x.EffectId == 190);
            var layout = device.SceneMidi;
            if (layout == null || midi.Record == null || midi.Record.Columns != layout.Columns || midi.Record.Rows != layout.Rows ||
                after.Blocks.Single(x => x.EffectId == 190).Record?.ShapeOffset != midi.Record.ShapeOffset)
                return Unknown("incomplete-scene-midi-layout");
            foreach (int bank in new[] { layout.TypeBank, layout.ChannelBank, layout.ControllerBank, layout.ValueBank })
                for (int s = 0; s < 8; s++)
                    scenes[s].Add(new(midi.Record.ShapeOffset + 2 * (layout.FirstWord + bank + 8 * s), 16));
        }
        if (scenes.SelectMany(x => x).Any(x => x.Offset < 0 || x.Length < 1 || x.Offset + x.Length > a.Length))
            return Unknown("incomplete-scene-layout");
        var intervals = scenes.SelectMany(x => x).OrderBy(x => x.Offset).ToArray();
        if (intervals.Zip(intervals.Skip(1)).Any(x => x.First.Offset + x.First.Length > x.Second.Offset))
            return Unknown("overlapping-scene-fields");
        int cursor = 0;
        foreach (var interval in intervals)
        {
            if (!a.AsSpan(cursor, interval.Offset - cursor).SequenceEqual(b.AsSpan(cursor, interval.Offset - cursor)))
                return Unknown("unexplained-body-change");
            cursor = interval.Offset + interval.Length;
        }
        if (!a.AsSpan(cursor).SequenceEqual(b.AsSpan(cursor))) return Unknown("unexplained-body-change");
        bool Correspond(int oldScene, int newScene) => scenes[oldScene].Zip(scenes[newScene])
            .All(x => a.AsSpan(x.First.Offset, x.First.Length).SequenceEqual(b.AsSpan(x.Second.Offset, x.Second.Length)));
        var candidates = Enumerable.Range(0, 8).Select(old => Enumerable.Range(0, 8).Where(next => Correspond(old, next)).ToArray()).ToArray();
        if (candidates.Any(x => x.Length == 0)) return Unknown("unexplained-body-change");
        int[] map = new int[8]; int[]? witness = null; int matches = 0;
        void Search(int oldScene, int used)
        {
            if (matches > 1) return;
            if (oldScene == 8)
            {
                byte[] projected = a.ToArray();
                for (int old = 0; old < 8; old++)
                    foreach (var pair in scenes[old].Zip(scenes[map[old]]))
                        a.AsSpan(pair.First.Offset, pair.First.Length).CopyTo(projected.AsSpan(pair.Second.Offset, pair.Second.Length));
                if (projected.AsSpan().SequenceEqual(b)) { matches++; witness = map.ToArray(); }
                return;
            }
            foreach (int next in candidates[oldScene])
                if ((used & (1 << next)) == 0)
                { map[oldScene] = next; Search(oldScene + 1, used | 1 << next); }
        }
        Search(0, 0);
        if (matches == 0) return Unknown("unexplained-body-change");
        if (matches > 1) return Unknown("ambiguous-identical-scenes");
        return new("exact-known-field-permutation", witness!.Select(x => x + 1).ToArray(), Scope);
    }
}
