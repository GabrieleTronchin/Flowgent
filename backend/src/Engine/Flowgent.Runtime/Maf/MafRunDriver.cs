namespace Flowgent.Runtime.Maf;

using System.Text.Json.Nodes;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.Logging;
using Decl = Microsoft.Agents.AI.Workflows.Declarative;
using DeclEvents = Microsoft.Agents.AI.Workflows.Declarative.Events;
using DefModel = Flowgent.Definitions.Model;
using RunContext = Flowgent.Runtime.Execution.RunContext;

/// <summary>
/// Drives one MAF declarative run (design §8, §11) and translates the declarative event stream into neutral
/// <see cref="MafRunEvent"/> records, so <c>RunOrchestrator</c> never sees a declarative type. It runs the workflow
/// with JSON checkpointing (the PROBE-B mechanism) so the host can read each step's <c>Global.&lt;stepId&gt;</c> JSON
/// value from run state and surface it as a <see cref="MafRunEvent.StepCompleted"/>; it bridges a <c>RequestInfoEvent</c>
/// human-input gate by invoking the supplied decision resolver and replying with an
/// <see cref="DeclEvents.ExternalInputResponse"/> carrying the <c>{decision, reason}</c> JSON.
/// </summary>
public sealed class MafRunDriver(ILogger<MafRunDriver> logger)
{
    private readonly ILogger<MafRunDriver> _logger = logger;

    /// <summary>Resolves the human decision for a gate on <paramref name="stepId"/>; cancellation ends the wait.</summary>
    public delegate Task<MafHumanDecision> ResolveDecisionAsync(string stepId, string? prompt, CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="built"/> to completion, invoking <paramref name="onEvent"/> for each neutral event and
    /// <paramref name="resolveDecision"/> when a human-input gate surfaces. Returns the terminal event (Completed or
    /// Failed); cancellation of <paramref name="ct"/> surfaces as an <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<MafRunEvent> RunAsync(
        RunContext run,
        MafWorkflowFactory.BuiltWorkflow built,
        Func<MafRunEvent, Task> onEvent,
        ResolveDecisionAsync resolveDecision,
        CancellationToken ct)
    {
        var checkpointDir = Directory.CreateTempSubdirectory($"flowgent-run-{run.RunId:N}-");
        try
        {
            return await RunCoreAsync(run, built, onEvent, resolveDecision, checkpointDir, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(checkpointDir);
        }
    }

    private async Task<MafRunEvent> RunCoreAsync(
        RunContext run,
        MafWorkflowFactory.BuiltWorkflow built,
        Func<MafRunEvent, Task> onEvent,
        ResolveDecisionAsync resolveDecision,
        DirectoryInfo checkpointDir,
        CancellationToken ct)
    {
        using var store = new FileSystemJsonCheckpointStore(checkpointDir);
        var checkpointManager = CheckpointManager.CreateJson(store);

        var execution = await InProcessExecution.RunStreamingAsync(built.Workflow, run.Input, checkpointManager, cancellationToken: ct).ConfigureAwait(false);

        MafRunEvent? terminal = null;
        var reportedOutputs = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var evt in execution.WatchStreamAsync(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case Decl.MessageActivityEvent activity:
                    _logger.LogDebug("Run {RunId} activity: {Message}", run.RunId, activity.Message);
                    break;

                case RequestInfoEvent request:
                    var (stepId, prompt) = DescribeRequest(request.Request);
                    await onEvent(new MafRunEvent.AwaitingInput(stepId, prompt)).ConfigureAwait(false);
                    var decision = await resolveDecision(stepId, prompt, ct).ConfigureAwait(false);
                    var answer = new DeclEvents.ExternalInputResponse(
                        new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, decision.ToJson().ToJsonString()));
                    await execution.SendResponseAsync(request.Request.CreateResponse(answer)).ConfigureAwait(false);
                    break;

                case SuperStepCompletedEvent step when step.CompletionInfo?.Checkpoint is not null:
                    await SurfaceOutputsAsync(run, checkpointDir, reportedOutputs, onEvent, ct).ConfigureAwait(false);
                    break;

                case WorkflowOutputEvent output:
                    terminal ??= ToCompleted(run, output);
                    break;

                case ExecutorFailedEvent failed:
                    terminal ??= new MafRunEvent.Failed(failed.ExecutorId, failed.Data?.Message ?? $"Step '{failed.ExecutorId}' failed.");
                    break;

                case WorkflowErrorEvent error:
                    terminal ??= new MafRunEvent.Failed(run.CurrentStepId, DescribeError(error.Data));
                    break;
            }
        }

        // Surface any final-step outputs produced in the last super-step.
        await SurfaceOutputsAsync(run, checkpointDir, reportedOutputs, onEvent, ct).ConfigureAwait(false);

        return terminal ?? new MafRunEvent.Failed(run.CurrentStepId, "Workflow completed without producing a result.");
    }

    private MafRunEvent.Completed ToCompleted(RunContext run, WorkflowOutputEvent output)
    {
        var data = output.Data;
        var node = data switch
        {
            null => null,
            JsonNode n => n,
            string s => PowerFxConversion.FromJsonString(s),
            _ => JsonValue.Create(data.ToString()),
        };

        // The reached terminal id and its success/failure outcome come from the manifest terminals[] on the descriptor.
        var terminalId = node is JsonObject obj && obj.TryGetPropertyValue("terminalId", out var tid) && tid is not null
            ? tid.GetValue<string>()
            : run.Workflow.Graph.Nodes.FirstOrDefault(n2 => n2.Kind == "terminal")?.Id ?? run.CurrentStepId ?? run.Workflow.Id;

        var outcome = ResolveOutcome(run.Workflow, terminalId);
        var message = node is JsonObject o2 && o2.TryGetPropertyValue("message", out var m) ? m?.GetValue<string>() : node?.ToString();
        return new MafRunEvent.Completed(terminalId, outcome, message, node);
    }

    private static DefModel.WorkflowOutcome ResolveOutcome(DefModel.WorkflowDescriptor workflow, string terminalId)
    {
        var node = workflow.Graph.Nodes.FirstOrDefault(n => n.Id == terminalId);
        return string.Equals(node?.Outcome, "failure", StringComparison.OrdinalIgnoreCase)
            ? DefModel.WorkflowOutcome.Failure
            : DefModel.WorkflowOutcome.Success;
    }

    private async Task SurfaceOutputsAsync(
        RunContext run,
        DirectoryInfo checkpointDir,
        HashSet<string> reported,
        Func<MafRunEvent, Task> onEvent,
        CancellationToken ct)
    {
        foreach (var (name, value) in await ReadGlobalsAsync(checkpointDir, ct).ConfigureAwait(false))
        {
            if (!reported.Add(name))
            {
                continue;
            }

            var node = PowerFxConversion.FromJsonString(value);

            // Global.assignedAgentId carries the dynamically assigned agent, not a step output.
            if (name == "assignedAgentId")
            {
                if (node is JsonValue v && v.TryGetValue<string>(out var agentId) && !string.IsNullOrWhiteSpace(agentId))
                {
                    run.AssignAgent(agentId);
                }

                continue;
            }

            run.SetOutput(name, node);
            await onEvent(new MafRunEvent.StepStarted(name)).ConfigureAwait(false);
            await onEvent(new MafRunEvent.StepCompleted(name, node, null)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the persisted <c>Global.*</c> variables from the JSON checkpoint files (PROBE-B): scope keys of the form
    /// <c>"&lt;actionId&gt;|@Global|&lt;name&gt;"</c> carry each variable's serialized value. Best-effort: files held open
    /// by the live store are skipped.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReadGlobalsAsync(DirectoryInfo checkpointDir, CancellationToken ct)
    {
        var globals = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!checkpointDir.Exists)
        {
            return globals;
        }

        foreach (var file in checkpointDir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Name.Equals("index.jsonl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text;
            try
            {
                using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            ExtractGlobals(text, globals);
        }

        return globals;
    }

    /// <summary>Pulls <c>@Global</c>-scoped variable name/value pairs out of a checkpoint JSON document.</summary>
    internal static void ExtractGlobals(string checkpointJson, IDictionary<string, string> into)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(checkpointJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        Walk(root);

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key.Contains("|@Global|", StringComparison.Ordinal) && value is not null)
                        {
                            var name = key[(key.LastIndexOf('|') + 1)..];
                            if (name.Length > 0)
                            {
                                into[name] = ReadValueCell(value);
                            }
                        }

                        Walk(value);
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Reads the stored value of a checkpointed <c>@Global</c> variable. The persisted form is
    /// <c>{ "typeId": {...}, "value": &lt;serialized value&gt; }</c>; the <c>value</c> cell is returned as a JSON string
    /// (a string cell verbatim, anything else serialized) so <see cref="PowerFxConversion.FromJsonString"/> can parse it.
    /// </summary>
    internal static string ReadValueCell(JsonNode stored)
    {
        var cell = stored is JsonObject obj && obj.TryGetPropertyValue("value", out var v) ? v : stored;
        return cell switch
        {
            null => "null",
            JsonValue jv when jv.TryGetValue<string>(out var s) => s,
            _ => cell.ToJsonString(),
        };
    }

    private static string DescribeError(object? data)
    {
        if (data is Exception ex)
        {
            var reason = ex.Message;
            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            {
                reason += $" -> {inner.Message}";
            }

            return reason;
        }

        return data?.ToString() ?? "Workflow error.";
    }

    private static (string StepId, string? Prompt) DescribeRequest(ExternalRequest request)
    {
        if (request.Data.Is<DeclEvents.ExternalInputRequest>(out var input) && input is not null)
        {
            return (request.PortInfo?.PortId ?? "approval", input.AgentResponse?.Text);
        }

        return (request.PortInfo?.PortId ?? "approval", null);
    }

    private static void TryDelete(DirectoryInfo dir)
    {
        try
        {
            if (dir.Exists)
            {
                dir.Delete(recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort; a transient hold on a checkpoint file must not fail the run.
        }
    }
}
