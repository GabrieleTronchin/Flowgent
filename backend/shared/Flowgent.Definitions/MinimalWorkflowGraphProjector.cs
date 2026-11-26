namespace Flowgent.Definitions;

using Flowgent.Definitions.Model;

/// <summary>
/// Fallback projector for callers that have no MAF-aware one (for example the Module SDK validation). It returns one node
/// per declared end state and no edges. The full graph comes from the projector in Engine.Core.
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
