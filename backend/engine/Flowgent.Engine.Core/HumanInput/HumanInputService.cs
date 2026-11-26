namespace Flowgent.Engine.Core.HumanInput;

using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Core.Runs;

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

public enum HumanInputResult
{
    Completed,
    NotFound,
    Conflict,
}
