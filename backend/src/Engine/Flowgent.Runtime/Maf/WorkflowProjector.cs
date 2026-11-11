namespace Flowgent.Runtime.Maf;

using System.Text.RegularExpressions;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;

/// <summary>
/// Projects a workflow's declarative YAML plus its manifest <c>terminals[]</c> into a <see cref="WorkflowGraphModel"/>
/// for the dashboard (design §10). The logical steps the dashboard shows are the actions that snapshot their output to
/// <c>Global.&lt;stepId&gt;</c> via a trailing <c>&lt;stepId&gt;_snapshot</c> SetVariable (dashboard kind taken from the
/// action with id <c>&lt;stepId&gt;</c>); all other actions (input/parse tools, detail/terminal plumbing SetVariables,
/// ConditionGroups, EndConversation) are excluded. Terminal nodes and their success/failure outcome come from the
/// manifest <c>terminals[]</c>, never inferred from the YAML. Lives in the seam (it parses the declarative document);
/// <see cref="ModuleLoader"/> depends only on <see cref="IWorkflowGraphProjector"/>.
/// </summary>
public sealed class WorkflowProjector : IWorkflowGraphProjector
{
    // Matches an action header "- kind: <Kind>" and a subsequent "id: <id>" line. A generic YAML parser cannot be used
    // here because action values are PowerFx expressions containing ": " inside quotes (valid for the declarative
    // builder's parser but not YamlDotNet); a line scan over the authored action list is robust and sufficient.
    private static readonly Regex KindLine = new(@"^\s*-\s*kind:\s*(\w+)\s*$", RegexOptions.Compiled);
    private static readonly Regex IdLine = new(@"^\s*id:\s*(\w[\w-]*)\s*$", RegexOptions.Compiled);

    public WorkflowGraphModel Project(WorkflowRef reference, string yaml)
    {
        // Scan the authored action list for (kind, id) pairs in order.
        var actionKinds = new Dictionary<string, string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        string? pendingKind = null;
        foreach (var line in yaml.Split('\n'))
        {
            var kindMatch = KindLine.Match(line);
            if (kindMatch.Success)
            {
                pendingKind = kindMatch.Groups[1].Value;
                continue;
            }

            var idMatch = IdLine.Match(line);
            if (idMatch.Success && pendingKind is not null)
            {
                var id = idMatch.Groups[1].Value;
                actionKinds[id] = pendingKind;
                ordered.Add(id);
                pendingKind = null;
            }
        }

        // A logical step is any action that snapshots its output to Global.<stepId> via a trailing SetVariable named
        // "<stepId>_snapshot"; its dashboard kind comes from the action whose id is <stepId>. Terminals come from the
        // manifest. This keeps the graph the logical pipeline the dashboard shows, excluding all plumbing actions.
        var nodes = new List<WorkflowGraphNode>();
        var sequence = new List<string>();

        foreach (var id in ordered)
        {
            if (!id.EndsWith("_snapshot", StringComparison.Ordinal))
            {
                continue;
            }

            var stepId = id[..^"_snapshot".Length];
            if (!nodes.Any(n => n.Id == stepId))
            {
                nodes.Add(new WorkflowGraphNode(stepId, stepId, MapKind(actionKinds.GetValueOrDefault(stepId)), null));
                sequence.Add(stepId);
            }
        }

        foreach (var terminal in reference.Terminals)
        {
            if (!nodes.Any(n => n.Id == terminal.Id))
            {
                nodes.Add(new WorkflowGraphNode(terminal.Id, terminal.Id, "terminal", terminal.Outcome.ToString().ToLowerInvariant()));
            }
        }

        var edges = new List<WorkflowGraphEdge>();
        AddSequentialEdges(sequence, edges);

        var start = sequence.Count > 0 ? sequence[0] : reference.Id;
        return new WorkflowGraphModel(start, nodes, edges);
    }

    private static string MapKind(string? kind) =>
        kind switch
        {
            "InvokeAzureAgent" => "llm",
            "InvokeMcpTool" => "tool",
            "Question" => "human-input",
            "ConditionGroup" => "rule",
            "SetVariable" => "map",
            _ => "map",
        };

    private static void AddSequentialEdges(List<string> sequence, List<WorkflowGraphEdge> edges)
    {
        for (var i = 0; i + 1 < sequence.Count; i++)
        {
            edges.Add(new WorkflowGraphEdge(sequence[i], sequence[i + 1], null));
        }
    }
}
