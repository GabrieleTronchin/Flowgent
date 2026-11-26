namespace Flowgent.Runtime.HumanInput;

using System.Collections.Concurrent;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Runtime.Runs;

/// <summary>Decision taken by an operator on a human-input step.</summary>
public sealed record HumanDecision(bool Approved, string? Reason)
{
    public string Value => Approved ? "approved" : "rejected";
}

/// <summary>A run waiting for a decision.</summary>
public sealed record PendingHumanInput(
    Guid RunId,
    string ModuleId,
    string WorkflowId,
    string StepId,
    string? RunTitle,
    ReviewPayload Review,
    DateTimeOffset RequestedAt,
    TaskCompletionSource<HumanDecision> Completion)
{
    public HumanInputInfo ToInfo() => new(RunId, ModuleId, WorkflowId, StepId, RunTitle, Review, RequestedAt);
}

/// <summary>Pending human inputs of all running workflows.</summary>
public sealed class HumanInputRegistry
{
    private readonly ConcurrentDictionary<Guid, PendingHumanInput> _pending = new();

    public void Register(PendingHumanInput pending) => _pending[pending.RunId] = pending;

    public void Remove(Guid runId) => _pending.TryRemove(runId, out _);

    public bool IsPending(Guid runId) => _pending.ContainsKey(runId);

    public IReadOnlyList<PendingHumanInput> GetPending(string? moduleId = null, string? workflowId = null) =>
        _pending.Values
            .Where(p => (moduleId is null || p.ModuleId == moduleId) && (workflowId is null || p.WorkflowId == workflowId))
            .OrderBy(p => p.RequestedAt)
            .ToList();

    public bool TryComplete(Guid runId, HumanDecision decision) =>
        _pending.TryGetValue(runId, out var pending) && pending.Completion.TrySetResult(decision);
}

public enum HumanInputResult
{
    Completed,
    NotFound,
    Conflict,
}

/// <summary>Approve / reject operations with the API status semantics (204, 404, 409).</summary>
public sealed class HumanInputService(HumanInputRegistry registry, IRunStore store)
{
    public Task<(HumanInputResult Result, string? Error)> ApproveAsync(Guid runId, CancellationToken ct = default) =>
        CompleteAsync(runId, new HumanDecision(true, null), ct);

    public Task<(HumanInputResult Result, string? Error)> RejectAsync(Guid runId, string? reason, CancellationToken ct = default) =>
        CompleteAsync(runId, new HumanDecision(false, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()), ct);

    private async Task<(HumanInputResult, string?)> CompleteAsync(Guid runId, HumanDecision decision, CancellationToken ct)
    {
        if (registry.TryComplete(runId, decision))
        {
            return (HumanInputResult.Completed, null);
        }

        var run = await store.GetAsync(runId, ct);
        if (run is null)
        {
            return (HumanInputResult.NotFound, $"Run {runId} not found.");
        }

        return run.Status.IsTerminal()
            ? (HumanInputResult.Conflict, $"Run {runId} has already completed ({run.Status}).")
            : (HumanInputResult.Conflict, $"Run {runId} is not awaiting input. Current status: {run.Status}.");
    }
}
