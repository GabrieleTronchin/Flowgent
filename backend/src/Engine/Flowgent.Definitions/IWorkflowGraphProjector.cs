namespace Flowgent.Definitions;

using Flowgent.Definitions.Model;

/// <summary>
/// Projects a workflow's declarative YAML plus its manifest-side outcome metadata into a
/// <see cref="WorkflowGraphModel"/> for the dashboard. Implemented in the Runtime MAF seam (which may parse the
/// declarative YAML); <see cref="ModuleLoader"/> only depends on this abstraction so the Definitions layer stays
/// free of declarative types.
/// </summary>
public interface IWorkflowGraphProjector
{
    WorkflowGraphModel Project(WorkflowRef reference, string yaml);
}

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
