namespace Flowgent.Contracts.Api;

public sealed record WorkflowGraph(string Start, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

public sealed record GraphNode(string Id, string Name, string Kind, string? Outcome);

public sealed record GraphEdge(string From, string To, string? Label);
