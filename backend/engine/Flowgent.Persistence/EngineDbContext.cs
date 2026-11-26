namespace Flowgent.Persistence;

using Flowgent.Contracts.Runs;
using Microsoft.EntityFrameworkCore;

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
