namespace Flowgent.Contracts.Api;

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
