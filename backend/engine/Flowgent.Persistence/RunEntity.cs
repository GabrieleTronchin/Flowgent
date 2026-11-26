namespace Flowgent.Persistence;

using Flowgent.Contracts.Runs;
using Microsoft.EntityFrameworkCore;

public sealed class RunEntity
{
    public Guid Id { get; set; }

    public string ModuleId { get; set; } = "";

    public string WorkflowId { get; set; } = "";

    public int WorkflowVersion { get; set; }

    public string? Title { get; set; }

    public string InputJson { get; set; } = "{}";

    public RunStatus Status { get; set; }

    public string? CurrentStepId { get; set; }

    public string? Detail { get; set; }

    public string? AssignedAgentId { get; set; }

    public string StepOutputsJson { get; set; } = "{}";

    public string? FinalOutputJson { get; set; }

    public Guid? InboxMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}
