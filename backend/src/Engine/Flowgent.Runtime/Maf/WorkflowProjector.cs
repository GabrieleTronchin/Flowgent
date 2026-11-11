namespace Flowgent.Runtime.Maf;

using System.Text.Json.Nodes;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Yaml;

/// <summary>
/// Projects a workflow's declarative YAML plus its manifest <c>terminals[]</c> into a <see cref="WorkflowGraphModel"/>
/// for the dashboard (design §10). One node per declarative action mapped to a dashboard kind
/// (<c>InvokeAzureAgent</c>→llm, <c>InvokeMcpTool</c>→tool, <c>Question</c>→human-input, <c>ConditionGroup</c>→rule,
/// <c>SetVariable</c>→map, terminal <c>SendActivity</c>/end→terminal); plumbing <c>SetVariable</c> nodes
/// (<c>*_snapshot</c>, <c>read_input</c>, <c>rt_*</c>) are excluded by naming convention. <c>ConditionGroup</c> branch
/// conditions become labelled edges, <c>elseActions</c> a default edge, and sequential actions an unlabelled edge.
/// The terminal outcome is tagged from the manifest, never inferred from the YAML. Lives in the seam (it parses the
/// declarative document); <see cref="ModuleLoader"/> depends only on <see cref="IWorkflowGraphProjector"/>.
/// </summary>
public sealed class WorkflowProjector : IWorkflowGraphProjector
{
    private static readonly string[] PlumbingPrefixes = ["rt_"];
    private static readonly string[] PlumbingExact = ["read_input"];

    public WorkflowGraphModel Project(WorkflowRef reference, string yaml)
    {
        var read = YamlJsonReader.Read(yaml);
        if (!read.IsSuccess || read.Root is not JsonObject root)
        {
            // Unparseable YAML is caught by the trial-build validator; fall back to the minimal graph.
            return MinimalWorkflowGraphProjector.Instance.Project(reference, yaml);
        }

        var outcomes = reference.Terminals.ToDictionary(t => t.Id, t => t.Outcome.ToString().ToLowerInvariant(), StringComparer.Ordinal);
        var actions = (root["trigger"] as JsonObject)?["actions"] as JsonArray ?? [];

        var nodes = new List<WorkflowGraphNode>();
        var edges = new List<WorkflowGraphEdge>();
        var sequence = new List<string>();

        foreach (var action in actions.OfType<JsonObject>())
        {
            var id = action["id"]?.GetValue<string>();
            var kind = action["kind"]?.GetValue<string>();
            if (id is null || kind is null || IsPlumbing(id, kind))
            {
                continue;
            }

            var dashboardKind = MapKind(kind, id, outcomes);
            var name = action["displayName"]?.GetValue<string>() ?? id;
            nodes.Add(new WorkflowGraphNode(id, name, dashboardKind, outcomes.GetValueOrDefault(id)));
            sequence.Add(id);

            if (kind == "ConditionGroup")
            {
                AddConditionEdges(id, action, edges);
            }
        }

        AddSequentialEdges(sequence, edges);

        var start = sequence.Count > 0 ? sequence[0] : reference.Id;
        return new WorkflowGraphModel(start, nodes, edges);
    }

    private static bool IsPlumbing(string id, string kind) =>
        kind == "SetVariable" &&
        (PlumbingExact.Contains(id, StringComparer.Ordinal)
            || id.EndsWith("_snapshot", StringComparison.Ordinal)
            || PlumbingPrefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal)));

    private static string MapKind(string kind, string id, IReadOnlyDictionary<string, string> outcomes) =>
        outcomes.ContainsKey(id)
            ? "terminal"
            : kind switch
            {
                "InvokeAzureAgent" => "llm",
                "InvokeMcpTool" => "tool",
                "Question" => "human-input",
                "ConditionGroup" => "rule",
                "SetVariable" => "map",
                "SendActivity" => "terminal",
                _ => "map",
            };

    private static void AddConditionEdges(string from, JsonObject action, List<WorkflowGraphEdge> edges)
    {
        if (action["conditions"] is JsonArray conditions)
        {
            foreach (var condition in conditions.OfType<JsonObject>())
            {
                var to = FirstGotoTarget(condition["actions"] as JsonArray);
                if (to is not null)
                {
                    edges.Add(new WorkflowGraphEdge(from, to, Friendly(condition["condition"]?.GetValue<string>())));
                }
            }
        }

        var elseTo = FirstGotoTarget(action["elseActions"] as JsonArray);
        if (elseTo is not null)
        {
            edges.Add(new WorkflowGraphEdge(from, elseTo, null));
        }
    }

    private static string? FirstGotoTarget(JsonArray? actions) =>
        actions?.OfType<JsonObject>()
            .Select(a => a["kind"]?.GetValue<string>() == "GotoAction" ? a["actionId"]?.GetValue<string>() : a["id"]?.GetValue<string>())
            .FirstOrDefault(id => id is not null);

    private static void AddSequentialEdges(List<string> sequence, List<WorkflowGraphEdge> edges)
    {
        for (var i = 0; i + 1 < sequence.Count; i++)
        {
            var from = sequence[i];
            var to = sequence[i + 1];
            if (!edges.Any(e => e.From == from))
            {
                edges.Add(new WorkflowGraphEdge(from, to, null));
            }
        }
    }

    /// <summary>Strips the PowerFx <c>=</c> prefix so a condition reads as a friendly edge label.</summary>
    internal static string? Friendly(string? condition) =>
        string.IsNullOrWhiteSpace(condition) ? null : condition.TrimStart('=').Trim();
}
