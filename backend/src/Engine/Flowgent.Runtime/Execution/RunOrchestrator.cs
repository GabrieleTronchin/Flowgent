namespace Flowgent.Runtime.Execution;

using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.HumanInput;
using Flowgent.Runtime.Maf;
using Flowgent.Runtime.Modules;
using Flowgent.Runtime.Runs;
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
/// Executes queued runs on MAF declarative workflows: builds the workflow for the run via
/// <see cref="MafWorkflowFactory"/>, drives it through the neutral <see cref="MafRunDriver"/> seam, records per-step
/// transitions through <see cref="RunRecorder"/> and exactly one terminal transition (Completed, Failed or Cancelled)
/// via the dual-write <see cref="IRunStore"/>. The gRPC RunMonitor shape, RunStatus set and current-step-id are
/// preserved because the orchestrator still drives RunRecorder/RunStore.
/// </summary>
public sealed class RunOrchestrator(
    IModuleCatalog catalog,
    IRunStore store,
    MafWorkflowFactory factory,
    MafRunDriver driver,
    RunRecorder recorder,
    HumanInputRegistry humanInput,
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
            humanInput.Remove(runId);
            registry.Unregister(runId);
        }
    }

    private async Task<RunExecutionResult> RunWorkflowAsync(RunContext run, CancellationToken runToken, CancellationToken hostToken)
    {
        // A fresh declarative workflow per run: the framework requires exclusive ownership and executors hold run state.
        var built = factory.Build(run);

        var terminal = await driver.RunAsync(
            run,
            built,
            evt => OnEventAsync(run, evt),
            (stepId, prompt, ct) => ResolveDecisionAsync(run, stepId, prompt, ct),
            runToken);

        // A cancelled step surfaces as a failure event: report it as abort/cancellation instead.
        if (runToken.IsCancellationRequested)
        {
            return await CancelledAsync(run, hostToken);
        }

        return terminal switch
        {
            MafRunEvent.Completed completed => await CompleteAsync(run, completed),
            MafRunEvent.Failed failed => await FailAsync(run, failed.StepId ?? run.CurrentStepId, failed.Reason),
            _ => await FailAsync(run, run.CurrentStepId, "Workflow completed without producing a result."),
        };
    }

    private async Task OnEventAsync(RunContext run, MafRunEvent evt)
    {
        switch (evt)
        {
            case MafRunEvent.StepStarted started:
                run.EnterStep(started.StepId);
                await recorder.StepStartedAsync(run, started.StepId);
                break;
            case MafRunEvent.StepCompleted completed:
                await recorder.StepCompletedAsync(run, completed.StepId, completed.Detail);
                break;
            case MafRunEvent.AwaitingInput awaiting:
                run.EnterStep(awaiting.StepId);
                await recorder.AwaitingInputAsync(run, awaiting.StepId, awaiting.Prompt);
                break;
        }
    }

    private async Task<MafHumanDecision> ResolveDecisionAsync(RunContext run, string stepId, string? prompt, CancellationToken ct)
    {
        var review = ReviewFromPrompt(prompt, stepId);
        var completion = new TaskCompletionSource<HumanDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        humanInput.Register(new PendingHumanInput(run.RunId, run.Module.Id, run.Workflow.Id, stepId, run.Title, review, DateTimeOffset.UtcNow, completion));

        HumanDecision decision;
        try
        {
            decision = await completion.Task.WaitAsync(ct);
        }
        finally
        {
            humanInput.Remove(run.RunId);
        }

        await recorder.InputReceivedAsync(run, stepId, decision.Reason is null ? decision.Value : $"{decision.Value}: {decision.Reason}");
        return new MafHumanDecision(decision.Approved, decision.Reason);
    }

    /// <summary>
    /// Builds the human-input review from the declarative <c>Question</c> prompt. The migrated workflow renders the
    /// review as a JSON object (<c>{ "title", "fields": [ { "label", "value" } ] }</c>) so the dashboard shows the same
    /// title and labelled fields the old <c>human-input</c> step did; a plain (non-JSON) prompt becomes a title-only review.
    /// </summary>
    private static ReviewPayload ReviewFromPrompt(string? prompt, string stepId)
    {
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            try
            {
                if (System.Text.Json.Nodes.JsonNode.Parse(prompt) is System.Text.Json.Nodes.JsonObject obj)
                {
                    var title = obj.TryGetPropertyValue("title", out var t) && t is not null ? t.GetValue<string>() : $"Approve step '{stepId}'?";
                    var fields = obj.TryGetPropertyValue("fields", out var f) && f is System.Text.Json.Nodes.JsonArray array
                        ? array.OfType<System.Text.Json.Nodes.JsonObject>()
                            .Select(field => new ReviewField(
                                field.TryGetPropertyValue("label", out var l) && l is not null ? l.GetValue<string>() : "",
                                field.TryGetPropertyValue("value", out var v) && v is not null ? v.GetValue<string>() : null))
                            .ToList()
                        : [];
                    return new ReviewPayload(title, fields);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not a structured review; fall through to the plain-prompt title.
            }
        }

        return new ReviewPayload(prompt ?? $"Approve step '{stepId}'?", []);
    }

    private async Task<RunExecutionResult> CompleteAsync(RunContext run, MafRunEvent.Completed outcome)
    {
        var status = outcome.Outcome == WorkflowOutcome.Success ? RunStatus.Completed : RunStatus.Failed;
        run.EnterStep(outcome.TerminalId);
        await store.RecordAsync(new RunTransition(
            run.RunId,
            status == RunStatus.Completed ? RunEventKind.Completed : RunEventKind.Failed,
            status,
            outcome.TerminalId,
            outcome.Message,
            run.AssignedAgentId,
            run.SnapshotOutputs(),
            outcome.Output));

        logger.LogInformation("Run {RunId} ended at {StepId} ({Status})", run.RunId, outcome.TerminalId, status);
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
