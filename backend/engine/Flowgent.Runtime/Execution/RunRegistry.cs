namespace Flowgent.Runtime.Execution;

using System.Collections.Concurrent;

/// <summary>
/// Running workflows, so they can be aborted. Aborting cancels the run token: in-flight LLM, tool and HTTP calls
/// and pending human input stop, and the orchestrator records a single Failed transition ("Aborted by user").
/// </summary>
public sealed class RunRegistry
{
    public const string AbortReason = "Aborted by user";
    public const string CancelledReason = "Workflow was cancelled.";

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runs = new();

    public bool IsRunning(Guid runId) => _runs.ContainsKey(runId);

    public int Count => _runs.Count;

    internal void Register(Guid runId, CancellationTokenSource cancellation) => _runs[runId] = cancellation;

    internal void Unregister(Guid runId) => _runs.TryRemove(runId, out _);

    /// <summary>Requests cancellation of a running workflow. Returns false if it is not running.</summary>
    public bool TryAbort(Guid runId)
    {
        if (!_runs.TryGetValue(runId, out var cts))
        {
            return false;
        }

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run completed while it was being aborted.
            return false;
        }
    }
}
