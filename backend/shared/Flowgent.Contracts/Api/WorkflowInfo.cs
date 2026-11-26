namespace Flowgent.Contracts.Api;

using System.Text.Json.Nodes;

public sealed record WorkflowInfo(
    string Id,
    string ModuleId,
    string Name,
    string? Description,
    int Version,
    bool Available,
    JsonNode? InputSchema,
    WorkflowGraph Graph,
    IReadOnlyList<SampleInfo> Samples);
