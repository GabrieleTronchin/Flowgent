namespace Flowgent.Contracts.Api;

using Flowgent.Contracts.Runs;

public sealed record RunEventInfo(
    Guid Id,
    Guid RunId,
    string ModuleId,
    string WorkflowId,
    RunEventKind Kind,
    string? PreviousStepId,
    string? StepId,
    RunStatus Status,
    DateTimeOffset Timestamp,
    string? Detail);
