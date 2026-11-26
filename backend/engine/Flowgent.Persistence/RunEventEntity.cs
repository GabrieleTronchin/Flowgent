namespace Flowgent.Persistence;

using Flowgent.Contracts.Runs;
using Microsoft.EntityFrameworkCore;

public sealed class RunEventEntity
{
    public Guid Id { get; set; }

    /// <summary>Monotonic order of events (timestamps can be equal).</summary>
    public long Sequence { get; set; }

    public Guid RunId { get; set; }

    public string ModuleId { get; set; } = "";

    public string WorkflowId { get; set; } = "";

    public RunEventKind Kind { get; set; }

    public string? PreviousStepId { get; set; }

    public string? StepId { get; set; }

    public RunStatus Status { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string? Detail { get; set; }
}
