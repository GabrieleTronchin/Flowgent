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
