namespace Flowgent.Persistence;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Runtime.Runs;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Runs and events with dual-write: every transition updates the run and appends its event in one SaveChanges,
/// then publishes the update to stream subscribers.
/// </summary>
internal sealed class EfRunStore(IDbContextFactory<EngineDbContext> dbContextFactory, RunUpdateChannel updates, SequenceGenerator sequence) : IRunStore
{
    public async Task CreateQueuedAsync(NewRun run, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        db.Runs.Add(new RunEntity
        {
            Id = run.RunId,
            ModuleId = run.ModuleId,
            WorkflowId = run.WorkflowId,
            WorkflowVersion = run.WorkflowVersion,
            Title = run.Title,
            InputJson = run.Input.ToJsonString(),
            Status = RunStatus.Queued,
            InboxMessageId = run.MessageId,
            CreatedAt = now,
            LastUpdated = now,
        });
        db.Events.Add(NewEvent(run.RunId, run.ModuleId, run.WorkflowId, RunEventKind.Queued, null, null, RunStatus.Queued, now, run.Title));
        db.InboxMessages.Add(new InboxMessageEntity
        {
            Id = run.MessageId,
            ModuleId = run.ModuleId,
            WorkflowId = run.WorkflowId,
            RunId = run.RunId,
            Payload = run.Input.ToJsonString(),
            ReceivedAt = now,
            Sequence = sequence.Next(),
        });

        await db.SaveChangesAsync(ct);
        updates.Publish(new RunUpdate(run.RunId, run.ModuleId, run.WorkflowId, null, RunStatus.Queued, RunEventKind.Queued, now, run.Title, run.Title));
    }

    public async Task<RunRecord> RecordAsync(RunTransition transition, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == transition.RunId, ct)
            ?? throw new InvalidOperationException($"Run {transition.RunId} not found.");
        var previousStepId = run.CurrentStepId;

        run.Status = transition.Status;
        run.CurrentStepId = transition.StepId ?? run.CurrentStepId;
        run.Detail = transition.Detail;
        run.LastUpdated = now;
        if (transition.AssignedAgentId is not null)
        {
            run.AssignedAgentId = transition.AssignedAgentId;
        }

        if (transition.StepOutputs is not null)
        {
            run.StepOutputsJson = transition.StepOutputs.ToJsonString();
        }

        if (transition.FinalOutput is not null)
        {
            run.FinalOutputJson = transition.FinalOutput.ToJsonString();
        }

        db.Events.Add(NewEvent(run.Id, run.ModuleId, run.WorkflowId, transition.Kind, previousStepId, run.CurrentStepId, transition.Status, now, transition.Detail));
        await db.SaveChangesAsync(ct);

        updates.Publish(new RunUpdate(run.Id, run.ModuleId, run.WorkflowId, run.CurrentStepId, run.Status, transition.Kind, now, run.Detail, run.Title));
        return ToRecord(run);
    }

    public async Task<RunRecord?> GetAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        return run is null ? null : ToRecord(run);
    }

    public async Task<IReadOnlyList<RunRecord>> ListAsync(RunQuery query, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var runs = db.Runs.AsNoTracking().AsQueryable();
        if (query.ModuleId is not null)
        {
            runs = runs.Where(r => r.ModuleId == query.ModuleId);
        }

        if (query.WorkflowId is not null)
        {
            runs = runs.Where(r => r.WorkflowId == query.WorkflowId);
        }

        if (query.Status is { } status)
        {
            runs = runs.Where(r => r.Status == status);
        }

        var entities = await runs.OrderByDescending(r => r.CreatedAt).Take(Math.Max(1, query.Limit)).ToListAsync(ct);
        return entities.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<RunEventInfo>> ListEventsAsync(EventQuery query, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var events = db.Events.AsNoTracking().AsQueryable();
        if (query.ModuleId is not null)
        {
            events = events.Where(e => e.ModuleId == query.ModuleId);
        }

        if (query.WorkflowId is not null)
        {
            events = events.Where(e => e.WorkflowId == query.WorkflowId);
        }

        if (query.RunId is { } runId)
        {
            events = events.Where(e => e.RunId == runId);
        }

        return await events
            .OrderByDescending(e => e.Sequence)
            .Take(Math.Max(1, query.Limit))
            .Select(e => new RunEventInfo(e.Id, e.RunId, e.ModuleId, e.WorkflowId, e.Kind, e.PreviousStepId, e.StepId, e.Status, e.Timestamp, e.Detail))
            .ToListAsync(ct);
    }

    public async Task<bool> AnyActiveAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.Runs.AnyAsync(r => r.Status == RunStatus.Running || r.Status == RunStatus.AwaitingInput, ct);
    }

    private RunEventEntity NewEvent(Guid runId, string moduleId, string workflowId, RunEventKind kind, string? previousStepId, string? stepId, RunStatus status, DateTimeOffset timestamp, string? detail) => new()
    {
        Id = Guid.NewGuid(),
        Sequence = sequence.Next(),
        RunId = runId,
        ModuleId = moduleId,
        WorkflowId = workflowId,
        Kind = kind,
        PreviousStepId = previousStepId,
        StepId = stepId,
        Status = status,
        Timestamp = timestamp,
        Detail = detail,
    };

    private static RunRecord ToRecord(RunEntity run) => new(
        run.Id,
        run.ModuleId,
        run.WorkflowId,
        run.WorkflowVersion,
        run.Title,
        JsonNode.Parse(run.InputJson) as JsonObject ?? [],
        run.Status,
        run.CurrentStepId,
        run.Detail,
        run.AssignedAgentId,
        JsonNode.Parse(run.StepOutputsJson) as JsonObject ?? [],
        run.FinalOutputJson is null ? null : JsonNode.Parse(run.FinalOutputJson),
        run.InboxMessageId,
        run.CreatedAt,
        run.LastUpdated);
}

internal sealed class EfInboxStore(IDbContextFactory<EngineDbContext> dbContextFactory) : IInboxStore
{
    public const string Queued = "queued";
    public const string Processed = "processed";
    public const string Failed = "failed";

    public async Task<IReadOnlyList<Guid>> GetPendingIdsAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.InboxMessages.AsNoTracking()
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.ReceivedAt).ThenBy(m => m.Sequence)
            .Select(m => m.Id)
            .ToListAsync(ct);
    }

    public async Task<InboxMessageRecord?> GetAsync(Guid messageId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var m = await db.InboxMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId, ct);
        return m is null ? null : new InboxMessageRecord(m.Id, m.ModuleId, m.WorkflowId, m.RunId, m.Payload, m.ReceivedAt, m.ProcessedAt, m.Error);
    }

    public async Task MarkProcessedAsync(Guid messageId, string? error, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var message = await db.InboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null)
        {
            return;
        }

        message.ProcessedAt = DateTimeOffset.UtcNow;
        message.Error = error;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<InboxMessageInfo>> ListAsync(string? status, string? moduleId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var query = db.InboxMessages.AsNoTracking().AsQueryable();
        if (moduleId is not null)
        {
            query = query.Where(m => m.ModuleId == moduleId);
        }

        query = status?.ToLowerInvariant() switch
        {
            Queued => query.Where(m => m.ProcessedAt == null),
            Processed => query.Where(m => m.ProcessedAt != null && m.Error == null),
            Failed => query.Where(m => m.Error != null),
            _ => query,
        };

        return await query
            .OrderByDescending(m => m.ReceivedAt).ThenByDescending(m => m.Sequence)
            .Select(m => new InboxMessageInfo(
                m.Id,
                m.ModuleId,
                m.WorkflowId,
                m.RunId,
                m.ReceivedAt,
                m.ProcessedAt,
                m.Error,
                m.ProcessedAt == null ? Queued : m.Error != null ? Failed : Processed,
                m.Payload))
            .ToListAsync(ct);
    }
}

/// <summary>Monotonic sequence numbers for ordering events and inbox messages.</summary>
internal sealed class SequenceGenerator
{
    private long _value;

    public long Next() => Interlocked.Increment(ref _value);
}
