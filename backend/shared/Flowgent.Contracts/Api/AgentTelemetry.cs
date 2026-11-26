namespace Flowgent.Contracts.Api;

public sealed record AgentTelemetry(
    string AgentId,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalCalls,
    double AverageLatencyMs,
    LlmCallInfo? LastCall);
