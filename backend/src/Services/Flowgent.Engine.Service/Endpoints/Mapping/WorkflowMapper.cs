namespace Flowgent.Engine.Service.Endpoints.Mapping;

using Flowgent.Contracts.Api;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Modules;

internal static class WorkflowMapper
{
    public static WorkflowInfo ToWorkflowInfo(ModuleState module, WorkflowDescriptor workflow)
    {
        var definition = module.Definition!;
        return new WorkflowInfo(
            workflow.Id,
            module.Id,
            workflow.Name,
            workflow.Description,
            workflow.Version,
            module.IsAvailable,
            definition.GetSchemaNode(workflow.InputSchema).DeepClone(),
            BuildGraph(workflow),
            definition.Samples
                .Where(s => s.Workflow == workflow.Id)
                .Select(s => new SampleInfo(s.Id, s.Name, s.Group, s.Description, s.Workflow, s.Input?.DeepClone(), s.ExpectInvalid))
                .ToList());
    }

    public static WorkflowGraph BuildGraph(WorkflowDescriptor workflow) => new(
        workflow.Graph.Start,
        workflow.Graph.Nodes.Select(n => new GraphNode(n.Id, n.Name, n.Kind, n.Outcome)).ToList(),
        workflow.Graph.Edges.Select(e => new GraphEdge(e.From, e.To, e.Label)).ToList());
}
