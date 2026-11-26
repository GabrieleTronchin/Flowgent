namespace Flowgent.Contracts.Runs;

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
