namespace Flowgent.Runtime.Runs;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;

/// <summary>A run as stored by the engine.</summary>
public sealed record RunRecord(
    Guid Id,
    string ModuleId,
    string WorkflowId,
    int WorkflowVersion,
    string? Title,
    JsonObject Input,
    RunStatus Status,
    string? CurrentStepId,
    string? Detail,
    string? AssignedAgentId,
    JsonObject StepOutputs,
    JsonNode? FinalOutput,
    Guid? InboxMessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdated)
{
    public RunSummary ToSummary() => new(Id, ModuleId, WorkflowId, WorkflowVersion, Title, Status, CurrentStepId, Detail, AssignedAgentId, CreatedAt, LastUpdated);

    public RunDetail ToDetail() => new(
        Id, ModuleId, WorkflowId, WorkflowVersion, Title, Status, CurrentStepId, Detail, AssignedAgentId, CreatedAt, LastUpdated,
        Input.DeepClone(), StepOutputs.DeepClone().AsObject(), FinalOutput?.DeepClone());
}

/// <summary>A new run and its inbox message, created together by the intake.</summary>
public sealed record NewRun(Guid RunId, Guid MessageId, string ModuleId, string WorkflowId, int WorkflowVersion, string? Title, JsonObject Input);

/// <summary>
/// A change of a run: moves it to a step and/or status and appends one event (dual-write).
/// Optional fields are only applied when set.
/// </summary>
public sealed record RunTransition(
    Guid RunId,
    RunEventKind Kind,
    RunStatus Status,
    string? StepId,
    string? Detail,
    string? AssignedAgentId = null,
    JsonObject? StepOutputs = null,
    JsonNode? FinalOutput = null);

public sealed record RunQuery(string? ModuleId = null, string? WorkflowId = null, RunStatus? Status = null, int Limit = 500);

public sealed record EventQuery(string? ModuleId = null, string? WorkflowId = null, Guid? RunId = null, int Limit = 200);

/// <summary>Persistence of runs and their transition events.</summary>
public interface IRunStore
{
    /// <summary>Creates the queued run, its <c>Queued</c> event and the inbox message in one operation.</summary>
    Task CreateQueuedAsync(NewRun run, CancellationToken ct = default);

    /// <summary>Updates the run and appends the event in one operation, then notifies subscribers.</summary>
    Task<RunRecord> RecordAsync(RunTransition transition, CancellationToken ct = default);

    Task<RunRecord?> GetAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<RunRecord>> ListAsync(RunQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<RunEventInfo>> ListEventsAsync(EventQuery query, CancellationToken ct = default);

    /// <summary>True when a run is running or awaiting input (sequential processing).</summary>
    Task<bool> AnyActiveAsync(CancellationToken ct = default);
}

public sealed record InboxMessageRecord(Guid Id, string ModuleId, string WorkflowId, Guid? RunId, string Payload, DateTimeOffset ReceivedAt, DateTimeOffset? ProcessedAt, string? Error);

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
