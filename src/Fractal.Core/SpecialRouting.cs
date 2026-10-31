namespace Fractal.Core;

public sealed record RoutingPort(string Node, int EffectId, string Role, string Endpoint, string Condition);
public sealed record ImplicitRoutingLink(string Source, string Destination, int Pair, string Kind);
public sealed record RoutingReachability(string Input, string[] Outputs);
public sealed record SceneRoutingReachability(int Scene, string Status, RoutingReachability[] Paths);
public sealed record SpecialRoutingReport(ImplicitRoutingLink[] Links, RoutingPort[] Ports, string[][] FeedbackComponents,
    RoutingReachability[] PotentialReachability, SceneRoutingReachability[] ScenePaths, string[] Issues, string Source, bool HardwareQualified = false);

/// <summary>Documented non-cable links and I/O roles. Potential paths are not an audibility claim.</summary>
public static class SpecialRouting
{
    public static SpecialRoutingReport Decode(DeviceDefinition device, RoutingRead grid, SavedBlockRead[]? blocks = null)
    {
        var links = new List<ImplicitRoutingLink>(); var issues = new List<string>(); var ports = new List<RoutingPort>();
        // Blocks Guide June 2023 pp89–90: two fixed pairs. Reserved enum instances are not extra documented pairs.
        for (int pair = 1; pair <= 2; pair++)
        {
            var sends = grid.Nodes.Where(n => n.EffectId == 181 + pair).ToArray();
            var returns = grid.Nodes.Where(n => n.EffectId == 185 + pair).ToArray();
            if (sends.Length == 1 && returns.Length == 1) links.Add(new(sends[0].Key, returns[0].Key, pair, "implicit-send-return"));
            else if (sends.Length + returns.Length != 0) issues.Add($"Send/Return pair {pair} is incomplete or ambiguous; no implicit link inferred.");
        }
        foreach (var node in grid.Nodes)
        {
            var identity = BlockCatalogue.Resolve(device.Model, node.EffectId);
            if (identity.IdentificationStatus == "unqualified-for-model" && identity.CandidateName?.StartsWith("Input", StringComparison.Ordinal) == true)
                issues.Add($"I/O identity {node.EffectId} is only a candidate for {device.Model}; no external source inferred.");
            if (node.EffectId is 184 or 185 or 188 or 189)
                issues.Add($"Reserved Send/Return identity {node.EffectId}: pair behavior is unmapped.");
            if (identity.Family is not ("Input" or "Output")) continue;
            if (identity.Availability.DocumentedAvailable != true)
            { issues.Add($"I/O identity {node.EffectId} lacks a documented endpoint for {device.Model}."); continue; }
            bool input = identity.Family == "Input";
            ports.Add(new(node.Key, node.EffectId, input ? "source" : "tap-and-grid-through", node.EffectId == 41 ? "USB input block" : identity.Name!,
                input ? "Engaged: external source replaces grid input; bypassed: grid input passes through. Digital source is global configuration."
                      : "Engaged: output tap and downstream grid path; bypassed: both muted. External cables/global source selections are not inferred."));
        }
        var all = (grid.Edges ?? []).Select(e => (e.Source, e.Destination))
            .Concat(links.Select(e => (e.Source, e.Destination))).Distinct().ToArray();
        var adjacency = grid.Nodes.ToDictionary(n => n.Key, _ => new List<string>());
        foreach (var (source, destination) in all) adjacency[source].Add(destination);
        HashSet<string> Reach(string start)
        {
            var seen = new HashSet<string>(); var stack = new Stack<string>(); stack.Push(start);
            while (stack.TryPop(out string? current))
                foreach (string next in adjacency[current]) if (seen.Add(next)) stack.Push(next);
            return seen;
        }
        var reach = adjacency.Keys.ToDictionary(k => k, Reach);
        var assigned = new HashSet<string>(); var components = new List<string[]>();
        foreach (string node in adjacency.Keys.Order(StringComparer.Ordinal))
        {
            if (assigned.Contains(node) || !reach[node].Contains(node)) continue;
            var component = reach[node].Where(other => reach[other].Contains(node)).Order(StringComparer.Ordinal).ToArray();
            assigned.UnionWith(component); components.Add(component);
        }
        var outputs = ports.Where(p => p.Role == "tap-and-grid-through").Select(p => p.Node).ToArray();
        var scenePaths = new List<SceneRoutingReachability>();
        for (int scene = 1; scene <= 8; scene++)
        {
            var bypass = ports.ToDictionary(p => p.Node, p => blocks?.SingleOrDefault(b => b.EffectId == p.EffectId)?.Scenes
                .SingleOrDefault(s => s.Scene == scene)?.Bypassed);
            if (grid.Edges == null || bypass.Values.Any(b => b == null))
            { scenePaths.Add(new(scene, "unknown-io-state", [])); continue; }
            var inputs = ports.Where(p => p.Role == "source").Select(p => p.Node).ToHashSet();
            var outputNodes = outputs.ToHashSet();
            var eligible = all.Where(e => !(inputs.Contains(e.Destination) && bypass[e.Destination] == false) &&
                !(outputNodes.Contains(e.Source) && bypass[e.Source] == true)).ToArray();
            var paths = new List<RoutingReachability>();
            foreach (string input in inputs.Where(i => bypass[i] == false).Order(StringComparer.Ordinal))
            {
                var seen = new HashSet<string> { input }; var stack = new Stack<string>(); stack.Push(input);
                while (stack.TryPop(out string? current))
                    foreach (var edge in eligible.Where(e => e.Source == current)) if (seen.Add(edge.Destination)) stack.Push(edge.Destination);
                paths.Add(new(input, outputs.Where(o => seen.Contains(o) && bypass[o] == false).Order(StringComparer.Ordinal).ToArray()));
            }
            scenePaths.Add(new(scene, grid.Status == "partial" || issues.Count != 0 ? "partial-io-conditioned-potential" : "io-conditioned-potential", paths.ToArray()));
        }
        return new(links.ToArray(), ports.ToArray(), components.ToArray(),
            ports.Where(p => p.Role == "source").Select(p => new RoutingReachability(p.Node,
                outputs.Where(o => reach[p.Node].Contains(o)).Order(StringComparer.Ordinal).ToArray())).ToArray(), scenePaths.ToArray(), issues.ToArray(),
            "fractal-blocks-guide: June 2023 pp4–6,89–90; block-identities model-qualified IDs");
    }
}
