namespace Flowgent.Definitions.Model;

/// <summary>
/// Definitions-layer projection of a workflow graph (start step, nodes and edges), built once at load time.
/// Distinct from the Contracts <c>WorkflowGraph</c> DTO: the engine endpoint maps this to the DTO.
/// </summary>
public sealed record WorkflowGraphModel(
    string Start,
    IReadOnlyList<WorkflowGraphNode> Nodes,
    IReadOnlyList<WorkflowGraphEdge> Edges);

/// <summary>A node in the projected graph: a logical step the dashboard shows.</summary>
/// <param name="Outcome">For terminal nodes: <c>success</c> or <c>failure</c>; otherwise null.</param>
public sealed record WorkflowGraphNode(string Id, string Name, string Kind, string? Outcome);

/// <summary>An edge between two steps, optionally labelled with the condition that selects it.</summary>
public sealed record WorkflowGraphEdge(string From, string To, string? Label);
