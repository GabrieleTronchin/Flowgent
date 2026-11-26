namespace Flowgent.Definitions;

using Flowgent.Definitions.Model;

/// <summary>
/// Turns a workflow YAML plus the outcome metadata of its manifest into the <see cref="WorkflowGraphModel"/> shown by the
/// dashboard. The real implementation lives in Engine.Core (it needs MAF to read the YAML); <see cref="ModuleLoader"/>
/// only knows this interface, so the Definitions project stays free of MAF.
/// </summary>
public interface IWorkflowGraphProjector
{
    WorkflowGraphModel Project(WorkflowRef reference, string yaml);
}
