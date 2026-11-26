namespace Flowgent.Contracts.Api;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Runs;

public sealed record RunDetail(
    Guid Id,
    string ModuleId,
    string WorkflowId,
    int WorkflowVersion,
    string? Title,
    RunStatus Status,
    string? CurrentStepId,
    string? Detail,
    string? AssignedAgentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdated,
    JsonNode? Input,
    JsonObject StepOutputs,
    JsonNode? FinalOutput);
