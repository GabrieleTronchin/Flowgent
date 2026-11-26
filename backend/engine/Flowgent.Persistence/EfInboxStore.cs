namespace Flowgent.Persistence;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Core.Runs;
using Microsoft.EntityFrameworkCore;

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
