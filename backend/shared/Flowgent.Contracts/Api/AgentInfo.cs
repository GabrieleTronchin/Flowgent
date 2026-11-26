namespace Flowgent.Contracts.Api;

public sealed record AgentInfo(
    string AgentId,
    string ModuleId,
    string Name,
    string? Group,
    string? Role,
    string Status,
    Guid? CurrentRunId,
    string? CurrentRunTitle,
    string? CurrentStepId);
