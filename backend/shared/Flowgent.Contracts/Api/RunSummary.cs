namespace Flowgent.Contracts.Api;

using Flowgent.Contracts.Runs;

public sealed record RunSummary(
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
    DateTimeOffset LastUpdated);
