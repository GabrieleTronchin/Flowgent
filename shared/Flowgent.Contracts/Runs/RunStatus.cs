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

/// <summary>Kind of a run transition event (audit log).</summary>
public enum RunEventKind
{
    Queued,
    StepStarted,
    StepCompleted,
    AwaitingInput,
    InputReceived,
    Completed,
    Failed,
    Cancelled,
}

public static class RunStatusExtensions
{
    public static IReadOnlyList<RunStatus> TerminalStatuses { get; } = [RunStatus.Completed, RunStatus.Failed, RunStatus.Cancelled];

    public static bool IsTerminal(this RunStatus status) =>
        status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled;
}
