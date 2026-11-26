namespace Flowgent.Definitions;

using Flowgent.Definitions.Model;

/// <summary>
/// Default projector used when no declarative-aware projector is injected (e.g. the Module SDK validation path).
/// Produces a minimal graph with one node per declared terminal and no edges. The real projection (action-kind
/// nodes, condition-labelled edges) is supplied by the Runtime seam.
/// </summary>
public sealed class MinimalWorkflowGraphProjector : IWorkflowGraphProjector
{
    public static readonly MinimalWorkflowGraphProjector Instance = new();

    public WorkflowGraphModel Project(WorkflowRef reference, string yaml)
    {
        var nodes = reference.Terminals
            .Select(t => new WorkflowGraphNode(t.Id, t.Id, "terminal", t.Outcome.ToString().ToLowerInvariant()))
            .ToList();

        return new WorkflowGraphModel(nodes.Count > 0 ? nodes[0].Id : reference.Id, nodes, []);
    }
}
