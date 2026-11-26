namespace Flowgent.Runtime.Execution;

using Flowgent.Contracts.Runs;
using Flowgent.Runtime.Runs;

/// <summary>Records run transitions (run update + event, dual-write) from the running workflow.</summary>
public sealed class RunRecorder(IRunStore store)
{
    public Task StepStartedAsync(RunContext run, string stepId) =>
        store.RecordAsync(new RunTransition(run.RunId, RunEventKind.StepStarted, RunStatus.Running, stepId, null));

    public Task StepCompletedAsync(RunContext run, string stepId, string? detail) =>
        store.RecordAsync(new RunTransition(
            run.RunId, RunEventKind.StepCompleted, RunStatus.Running, stepId, detail, run.AssignedAgentId, run.SnapshotOutputs()));

    public Task AwaitingInputAsync(RunContext run, string stepId, string? detail) =>
        store.RecordAsync(new RunTransition(run.RunId, RunEventKind.AwaitingInput, RunStatus.AwaitingInput, stepId, detail));

    public Task InputReceivedAsync(RunContext run, string stepId, string detail) =>
        store.RecordAsync(new RunTransition(run.RunId, RunEventKind.InputReceived, RunStatus.Running, stepId, detail));
}
