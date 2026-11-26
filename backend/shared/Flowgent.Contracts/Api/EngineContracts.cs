namespace Flowgent.Contracts.Api;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Runs;

/// <summary>Routes of the engine API (all under <see cref="Base"/>).</summary>
public static class EngineRoutes
{
    public const string Base = "/api/engine";

    public static string SubmitRun(string workflowId) => $"{Base}/workflows/{Uri.EscapeDataString(workflowId)}/runs";

    public static string Run(Guid runId) => $"{Base}/runs/{runId}";
}

/// <summary><c>202 Accepted</c> body of an intake request.</summary>
public sealed record IntakeAccepted(Guid MessageId, Guid RunId);

public enum ModuleStatus
{
    Loading,
    Available,
    Invalid,
    Unavailable,
}

public enum DefinitionSeverity
{
    Error,
    Warning,
}

public sealed record DefinitionErrorInfo(
    string ModuleId,
    string? File,
    string? StepId,
    int? Line,
    string Message,
    DefinitionSeverity Severity);

public sealed record ModuleInfo(
    string Id,
    string Name,
    string? Description,
    ModuleStatus Status,
    string? Version,
    IReadOnlyList<string> Workflows,
    IReadOnlyList<DefinitionErrorInfo> Errors,
    DateTimeOffset? LastLoadedAt,
    string? LastError);

public sealed record GraphNode(string Id, string Name, string Kind, string? Outcome);

public sealed record GraphEdge(string From, string To, string? Label);

public sealed record WorkflowGraph(string Start, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

public sealed record SampleInfo(
    string Id,
    string Name,
    string? Group,
    string? Description,
    string WorkflowId,
    JsonNode? Input,
    bool ExpectInvalid);

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

public sealed record ReviewField(string Label, string? Value);

public sealed record ReviewPayload(string Title, IReadOnlyList<ReviewField> Fields);

public sealed record HumanInputInfo(
    Guid RunId,
    string ModuleId,
    string WorkflowId,
    string StepId,
    string? RunTitle,
    ReviewPayload Review,
    DateTimeOffset RequestedAt);

public sealed record RejectRequest(string? Reason);

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

public sealed record InboxMessageInfo(
    Guid Id,
    string ModuleId,
    string WorkflowId,
    Guid? RunId,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string? Error,
    string Status,
    string? Payload);

public sealed record RuntimeConfigInfo(bool SequentialProcessing, bool DemoMode, string LlmProvider, string LlmModel);

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

public sealed record AgentTelemetry(
    string AgentId,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalCalls,
    double AverageLatencyMs,
    LlmCallInfo? LastCall);

public sealed record TelemetrySummary(int TotalTokens, int TotalCalls, double AverageLatencyMs, double ErrorRate);
