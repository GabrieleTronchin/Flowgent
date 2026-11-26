namespace Flowgent.Engine.Core.Runs.Store;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;

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

/// <summary>A new run and its inbox message, created together by the intake.</summary>
public sealed record NewRun(Guid RunId, Guid MessageId, string ModuleId, string WorkflowId, int WorkflowVersion, string? Title, JsonObject Input);

public sealed record RunQuery(string? ModuleId = null, string? WorkflowId = null, RunStatus? Status = null, int Limit = 500);

public sealed record EventQuery(string? ModuleId = null, string? WorkflowId = null, Guid? RunId = null, int Limit = 200);
