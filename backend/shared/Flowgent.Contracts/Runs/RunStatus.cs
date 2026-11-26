namespace Flowgent.Contracts.Runs;

/// <summary>Lifecycle of a run.</summary>
public enum RunStatus
{
    Queued,
    Running,
    AwaitingInput,
    Completed,
    Failed,
    Cancelled,
}
