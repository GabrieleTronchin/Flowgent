namespace Flowgent.Engine.Core.Runs;

using Flowgent.Contracts.Api;

/// <summary>Persistence of the inbox (transactional intake queue).</summary>
public interface IInboxStore
{
    /// <summary>Ids of unprocessed messages, oldest first.</summary>
    Task<IReadOnlyList<Guid>> GetPendingIdsAsync(CancellationToken ct = default);

    Task<InboxMessageRecord?> GetAsync(Guid messageId, CancellationToken ct = default);

    Task MarkProcessedAsync(Guid messageId, string? error, CancellationToken ct = default);

    /// <summary>Messages, newest first; <paramref name="status"/>: queued, processed or failed.</summary>
    Task<IReadOnlyList<InboxMessageInfo>> ListAsync(string? status, string? moduleId, CancellationToken ct = default);
}

public sealed record InboxMessageRecord(Guid Id, string ModuleId, string WorkflowId, Guid? RunId, string Payload, DateTimeOffset ReceivedAt, DateTimeOffset? ProcessedAt, string? Error);
