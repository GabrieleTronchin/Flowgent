namespace Flowgent.Persistence;

using Flowgent.Contracts.Runs;
using Microsoft.EntityFrameworkCore;

public sealed class InboxMessageEntity
{
    public Guid Id { get; set; }

    public string ModuleId { get; set; } = "";

    public string WorkflowId { get; set; } = "";

    public Guid? RunId { get; set; }

    public string Payload { get; set; } = "";

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Monotonic order of arrival (FIFO when timestamps are equal).</summary>
    public long Sequence { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? Error { get; set; }
}
