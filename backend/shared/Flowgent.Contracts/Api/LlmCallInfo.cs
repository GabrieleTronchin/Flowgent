namespace Flowgent.Contracts.Api;

public sealed record LlmCallInfo(
    string ModelName,
    int PromptTokens,
    int CompletionTokens,
    long LatencyMs,
    bool Success,
    DateTimeOffset Timestamp,
    string? ErrorMessage,
    string? AgentId,
    string? ModuleId,
    string? WorkflowId,
    Guid? RunId,
    string? StepId);
