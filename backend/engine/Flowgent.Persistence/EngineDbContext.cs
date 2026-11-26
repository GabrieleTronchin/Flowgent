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

/// <summary>Engine database (EF Core InMemory today; the model is ready for a relational provider).</summary>
public sealed class EngineDbContext(DbContextOptions<EngineDbContext> options) : DbContext(options)
{
    public DbSet<RunEntity> Runs => Set<RunEntity>();

    public DbSet<RunEventEntity> Events => Set<RunEventEntity>();

    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RunEntity>(run =>
        {
            run.HasKey(r => r.Id);
            run.Property(r => r.ModuleId).HasMaxLength(64).IsRequired();
            run.Property(r => r.WorkflowId).HasMaxLength(64).IsRequired();
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            run.Property(r => r.CurrentStepId).HasMaxLength(64);
            run.HasIndex(r => new { r.ModuleId, r.WorkflowId, r.Status });
        });

        modelBuilder.Entity<RunEventEntity>(evt =>
        {
            evt.HasKey(e => e.Id);
            evt.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32);
            evt.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            evt.HasIndex(e => e.RunId);
            evt.HasIndex(e => e.Sequence);
        });

        modelBuilder.Entity<InboxMessageEntity>(message =>
        {
            message.HasKey(m => m.Id);
            message.Property(m => m.Payload).IsRequired();
            message.HasIndex(m => new { m.ProcessedAt, m.Sequence });
        });
    }
}
