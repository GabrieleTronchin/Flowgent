namespace Flowgent.Contracts.Api;

using System.Text.Json.Nodes;

public sealed record SampleInfo(
    string Id,
    string Name,
    string? Group,
    string? Description,
    string WorkflowId,
    JsonNode? Input,
    bool ExpectInvalid);
