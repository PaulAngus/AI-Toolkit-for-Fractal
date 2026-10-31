using System.Text.Json;
namespace Fractal.Core;

public sealed record GridPoint(int Row, int Column);
public sealed record AudioPathRequest(int SchemaVersion, string Model, int Capacity, string Firmware, int Slot,
    string BaseSha256, string Persistence, GridPoint[] Waypoints, int ShuntId, string SharingPolicy = "isolated", string FeedbackPolicy = "preserve");
public sealed record AudioPathResult(GridPoint[] Path, PresetEditPlan Plan, int AddedShunts, int AddedEdges);

/// <summary>Deterministic forward grid path through explicit waypoints; produces ordinary edit operations.</summary>
public static class AutomaticRouting
{
    public static AudioPathRequest Parse(byte[] json)
    {
        if (json.Length > 65536) throw new InvalidDataException("Audio path request exceeds bound.");
        return JsonSerializer.Deserialize<AudioPathRequest>(json, LiveControlScope.JsonOptions) ?? throw new InvalidDataException("Missing audio path request.");
    }
    public static AudioPathResult Create(AudioPathRequest input, byte[] capture)
    {
        var request = Parse(JsonSerializer.SerializeToUtf8Bytes(input, LiveControlScope.JsonOptions));
        var d = DeviceDefinition.Create(request.Model, request.Capacity); d.ValidateSlot(request.Slot);
        if (request.SchemaVersion != 1 || request.Waypoints == null || request.Waypoints.Length is < 2 or > 14 ||
            request.Waypoints.Any(p => p == null || p.Row < 1 || p.Row > d.GridRows || p.Column < 1 || p.Column > d.GridColumns) ||
            request.Waypoints.Zip(request.Waypoints.Skip(1)).Any(p => p.First.Column >= p.Second.Column) ||
            request.ShuntId is < 1024 or > 1029 || request.SharingPolicy is not ("isolated" or "allow-existing"))
            throw new ArgumentException("Choose 2–14 increasing-column waypoints, explicit shunt ID 1024–1029, and isolated/allow-existing sharing.");
        var projection = PresetEditProjection.Decode(d, request.Firmware, capture);
        if (projection.Slot != request.Slot || projection.SysexSha256 != request.BaseSha256) throw new InvalidDataException("Path baseline slot/hash mismatch.");
        var words = projection.Routing.Words.ToDictionary(w => new GridPoint(w.Row, w.Column));
        if (words.Count != d.GridRows * d.GridColumns) throw new InvalidDataException("Complete grid required.");
        var explicitPoints = request.Waypoints.ToHashSet();
        bool Shunt(GridPoint p) => words[p].EffectId is >= 1024 and <= 1029;
        int[] Outgoing(GridPoint p) => p.Column == d.GridColumns ? [] : Enumerable.Range(1, d.GridRows)
            .Where(row => (words[new(row, p.Column + 1)].RawWord & (1 << (p.Row - 1))) != 0).ToArray();
        foreach (var p in request.Waypoints)
            if (words[p].EffectId == 0 || words[p].EffectId == 190 || !Shunt(p) && BlockCatalogue.Resolve(d.Model, words[p].EffectId).Name == null)
                throw new InvalidDataException("Waypoints must be occupied known audio blocks or shunts; automatic routing does not create effects or use Scene MIDI as audio.");
        bool Eligible(GridPoint p)
        {
            var w = words[p];
            if (explicitPoints.Contains(p)) return true;
            if (w.UnknownBits != 0) return false;
            return Shunt(p) || w.EffectId == 0 && w.RawWord == 0 && Outgoing(p).Length == 0;
        }
        bool Connectable(GridPoint a, GridPoint b)
        {
            if (words[b].UnknownBits != 0) return false;
            if (request.SharingPolicy == "allow-existing") return true;
            if (!explicitPoints.Contains(a) && Outgoing(a).Any(row => row != b.Row)) return false;
            if (!explicitPoints.Contains(b) && (words[b].RawWord & ~(1 << (a.Row - 1))) != 0) return false;
            return true;
        }
        var path = new List<GridPoint> { request.Waypoints[0] };
        for (int segment = 1; segment < request.Waypoints.Length; segment++)
        {
            var start = request.Waypoints[segment - 1]; var end = request.Waypoints[segment];
            var best = new Dictionary<int, Candidate> { [start.Row] = new([start], 0, 0, 0) };
            for (int column = start.Column + 1; column <= end.Column; column++)
            {
                var next = new Dictionary<int, Candidate>();
                foreach (int row in column == end.Column ? new[] { end.Row } : Enumerable.Range(1, d.GridRows))
                {
                    var point = new GridPoint(row, column); if (!Eligible(point)) continue;
                    foreach (var prior in best.Values)
                    {
                        var last = prior.Path[^1]; if (!Connectable(last, point)) continue;
                        var candidate = new Candidate([.. prior.Path, point], prior.Shunts + (words[point].EffectId == 0 ? 1 : 0),
                            prior.Edges + ((words[point].RawWord & (1 << (last.Row - 1))) == 0 ? 1 : 0), prior.Movement + Math.Abs(last.Row - row));
                        if (!next.TryGetValue(row, out var previous) || candidate.Compare(previous) < 0) next[row] = candidate;
                    }
                }
                best = next;
            }
            if (!best.TryGetValue(end.Row, out var chosen)) throw new InvalidDataException("No forward path satisfies occupied cells, known routing bits and the explicit sharing policy.");
            path.AddRange(chosen.Path.Skip(1));
        }
        var operations = new List<PresetEditOperation>(); int shunts = 0, edges = 0;
        foreach (var point in path.Where(p => words[p].EffectId == 0))
        { operations.Add(new("insert-shunt", EffectId: request.ShuntId, Row: point.Row, Column: point.Column)); shunts++; }
        foreach (var pair in path.Zip(path.Skip(1)))
        {
            operations.Add(new("connect", SourceRow: pair.First.Row, SourceColumn: pair.First.Column, DestinationRow: pair.Second.Row, DestinationColumn: pair.Second.Column));
            if ((words[pair.Second].RawWord & (1 << (pair.First.Row - 1))) == 0) edges++;
        }
        var plan = PresetEditPlan.Create(new(1, d.Model, d.PresetCapacity, request.Firmware, request.Slot, request.Persistence,
            request.BaseSha256, operations.ToArray(), request.FeedbackPolicy), capture);
        return new(path.ToArray(), plan, shunts, edges);
    }
    private sealed record Candidate(GridPoint[] Path, int Shunts, int Edges, int Movement)
    {
        public int Compare(Candidate other)
        {
            int value = Shunts.CompareTo(other.Shunts); if (value != 0) return value;
            value = Edges.CompareTo(other.Edges); if (value != 0) return value;
            value = Movement.CompareTo(other.Movement); if (value != 0) return value;
            for (int i = 0; i < Path.Length; i++) { value = Path[i].Row.CompareTo(other.Path[i].Row); if (value != 0) return value; }
            return 0;
        }
    }
}
