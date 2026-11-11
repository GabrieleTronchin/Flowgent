namespace Flowgent.Runtime.Execution;

using Flowgent.Contracts.Runs;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Modules;
using Flowgent.Runtime.Runs;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using RunStatus = Flowgent.Contracts.Runs.RunStatus;

/// <summary>Result of executing a queued run.</summary>
/// <param name="StartError">Set when the run could not start (unknown or unavailable workflow); recorded on the inbox message.</param>
public sealed record RunExecutionResult(RunStatus Status, string? StartError = null);

/// <summary>Executes queued runs (implemented by <see cref="RunOrchestrator"/>; faked in inbox tests).</summary>
public interface IRunExecutor
{
    Task<RunExecutionResult> ExecuteAsync(Guid runId, CancellationToken hostToken = default);
}

/// <summary>
/// Executes queued runs on Agent Framework: compiles the workflow for the run, streams its events and records
/// exactly one terminal transition (Completed, Failed or Cancelled).
/// </summary>
public sealed class RunOrchestrator(
    IModuleCatalog catalog,
    IRunStore store,
    WorkflowCompiler compiler,
    RunRegistry registry,
    ILogger<RunOrchestrator> logger) : IRunExecutor
{
    public async Task<RunExecutionResult> ExecuteAsync(Guid runId, CancellationToken hostToken = default)
    {
        var record = await store.GetAsync(runId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Run {runId} not found.");

        if (record.Status.IsTerminal())
        {
            // Aborted while queued.
            return new RunExecutionResult(record.Status);
        }

        var handle = catalog.FindWorkflow(record.WorkflowId);
        if (handle is null || handle.Module.Id != record.ModuleId)
        {
            return await FailToStartAsync(record, $"Workflow '{record.WorkflowId}' is not available.");
        }

        if (!handle.IsAvailable)
        {
            return await FailToStartAsync(record, $"Module '{record.ModuleId}' is unavailable.");
        }

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        registry.Register(runId, runCts);

        var run = new RunContext(runId, handle.Definition, handle.Workflow, record.Input, record.Title, runCts.Token);
        logger.LogInformation("Starting run {RunId} of workflow {WorkflowId} (module {ModuleId}, version {Version})",
            runId, handle.Workflow.Id, handle.Module.Id, handle.Definition.Version);

        try
        {
            return await RunWorkflowAsync(run, runCts.Token, hostToken);
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            return await CancelledAsync(run, hostToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} failed", runId);
            return await FailAsync(run, run.CurrentStepId, ex.Message);
        }
        finally
        {
            registry.Unregister(runId);
        }
    }

    private async Task<RunExecutionResult> RunWorkflowAsync(RunContext run, CancellationToken runToken, CancellationToken hostToken)
    {
        // A fresh workflow per run: the framework requires exclusive ownership and executors hold run state.
        var workflow = compiler.Build(run);
        await using var execution = await InProcessExecution.RunStreamingAsync(workflow, new StepMessage(run.Workflow.Graph.Start), $"run-{run.RunId}", runToken);

        // Executors receive the framework's own token: propagate abort/shutdown to the run explicitly.
        await using var cancelRegistration = runToken.Register(() => _ = execution.CancelRunAsync());

        await execution.TrySendMessageAsync(new TurnToken(emitEvents: true));

        RunOutcome? outcome = null;
        (string? StepId, string Reason)? failure = null;

        await foreach (var evt in execution.WatchStreamAsync(runToken))
        {
            switch (evt)
            {
                case WorkflowOutputEvent { Data: RunOutcome result }:
                    outcome = result;
                    break;
                case ExecutorFailedEvent failed:
                    failure ??= (failed.ExecutorId, failed.Data?.Message ?? $"Step '{failed.ExecutorId}' failed.");
                    logger.LogWarning(failed.Data, "Step {StepId} failed in run {RunId}", failed.ExecutorId, run.RunId);
                    break;
                case WorkflowErrorEvent error:
                    var exception = error.Data as Exception;
                    failure ??= (exception is StepFailedException sf ? sf.StepId : run.CurrentStepId, exception?.Message ?? "Workflow error.");
                    logger.LogWarning(exception, "Workflow error in run {RunId}", run.RunId);
                    break;
            }
        }

        // A cancelled step surfaces as a failure event: report it as abort/cancellation instead.
        if (runToken.IsCancellationRequested)
        {
            return await CancelledAsync(run, hostToken);
        }

        if (outcome is not null)
        {
            return await CompleteAsync(run, outcome);
        }

        return await FailAsync(run, failure?.StepId ?? run.CurrentStepId, failure?.Reason ?? "Workflow completed without producing a result.");
    }

    private async Task<RunExecutionResult> CompleteAsync(RunContext run, RunOutcome outcome)
    {
        var status = outcome.Outcome == WorkflowOutcome.Success ? RunStatus.Completed : RunStatus.Failed;
        await store.RecordAsync(new RunTransition(
            run.RunId,
            status == RunStatus.Completed ? RunEventKind.Completed : RunEventKind.Failed,
            status,
            outcome.StepId,
            outcome.Message,
            run.AssignedAgentId,
            run.SnapshotOutputs(),
            outcome.Output));

        logger.LogInformation("Run {RunId} ended at {StepId} ({Status})", run.RunId, outcome.StepId, status);
        return new RunExecutionResult(status);
    }

    private async Task<RunExecutionResult> CancelledAsync(RunContext run, CancellationToken hostToken)
    {
        if (hostToken.IsCancellationRequested)
        {
            await store.RecordAsync(new RunTransition(run.RunId, RunEventKind.Cancelled, RunStatus.Cancelled, run.CurrentStepId, RunRegistry.CancelledReason));
            logger.LogWarning("Run {RunId} cancelled by host shutdown", run.RunId);
            return new RunExecutionResult(RunStatus.Cancelled);
        }

        logger.LogWarning("Run {RunId} aborted by user", run.RunId);
        return await FailAsync(run, run.CurrentStepId, RunRegistry.AbortReason);
    }

    private async Task<RunExecutionResult> FailAsync(RunContext run, string? stepId, string reason)
    {
        await store.RecordAsync(new RunTransition(
            run.RunId, RunEventKind.Failed, RunStatus.Failed, stepId, reason, run.AssignedAgentId, run.SnapshotOutputs()));
        return new RunExecutionResult(RunStatus.Failed);
    }

    private async Task<RunExecutionResult> FailToStartAsync(RunRecord record, string reason)
    {
        logger.LogWarning("Run {RunId} could not start: {Reason}", record.Id, reason);
        await store.RecordAsync(new RunTransition(record.Id, RunEventKind.Failed, RunStatus.Failed, null, reason));
        return new RunExecutionResult(RunStatus.Failed, reason);
    }
}
