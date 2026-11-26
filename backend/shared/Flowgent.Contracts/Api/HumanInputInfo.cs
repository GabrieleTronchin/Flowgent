namespace Flowgent.Contracts.Api;

public sealed record HumanInputInfo(
    Guid RunId,
    string ModuleId,
    string WorkflowId,
    string StepId,
    string? RunTitle,
    ReviewPayload Review,
    DateTimeOffset RequestedAt);
