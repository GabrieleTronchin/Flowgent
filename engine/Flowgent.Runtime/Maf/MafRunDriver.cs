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

                case ExecutorFailedEvent failed:
                    terminal ??= new MafRunEvent.Failed(failed.ExecutorId, DescribeError(failed.Data) ?? $"Step '{failed.ExecutorId}' failed.");
                    break;

                case WorkflowErrorEvent error:
                    terminal ??= new MafRunEvent.Failed(run.CurrentStepId, DescribeError(error.Data) ?? "Workflow error.");
                    break;
            }
        }

        // Surface any final-step outputs produced in the last super-step, then read the terminal from run state.
        await SurfaceOutputsAsync(run, checkpointDir, reportedOutputs, onEvent, ct).ConfigureAwait(false);

        if (terminal is not null)
        {
            return terminal;
        }

        MafRunEvent? completed = ToCompleted(run, await ReadGlobalsAsync(checkpointDir, ct).ConfigureAwait(false));
        return completed ?? new MafRunEvent.Failed(run.CurrentStepId, "Workflow completed without producing a result.");
    }

    /// <summary>
    /// Builds the terminal event from the run-state <c>Global.*</c> variables (design §8). A migrated terminal writes
    /// three reserved globals via trailing <c>SetVariable</c>: <c>Global.__terminalId</c> (the reached terminal action
    /// id), <c>Global.__message</c> (the terminal message/detail, verbatim) and optionally <c>Global.__finalOutput</c>
    /// (the structured JSON a success terminal exposes as <c>FinalOutput</c>). The success/failure outcome is taken from
    /// the descriptor manifest <c>terminals[]</c> for that id, never inferred from the YAML.
    /// </summary>
    private static MafRunEvent.Completed? ToCompleted(RunContext run, IReadOnlyDictionary<string, string> globals)
    {
        if (!globals.TryGetValue("__terminalId", out var rawId))
        {
            return null;
        }

        var terminalId = PowerFxConversion.FromJsonString(rawId) is JsonValue idv && idv.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id)
            ? id
            : run.Workflow.Graph.Nodes.FirstOrDefault(n => n.Kind == "terminal")?.Id ?? run.CurrentStepId ?? run.Workflow.Id;

        var outcome = ResolveOutcome(run.Workflow, terminalId);

        string? message = null;
        if (globals.TryGetValue("__message", out var rawMessage) && PowerFxConversion.FromJsonString(rawMessage) is JsonValue mv && mv.TryGetValue<string>(out var m))
        {
            message = m;
        }

        var finalOutput = globals.TryGetValue("__finalOutput", out var rawOut) ? PowerFxConversion.FromJsonString(rawOut) : null;
        return new MafRunEvent.Completed(terminalId, outcome, message, finalOutput);
    }

    private static DefModel.WorkflowOutcome ResolveOutcome(DefModel.WorkflowDescriptor workflow, string terminalId)
    {
        var node = workflow.Graph.Nodes.FirstOrDefault(n => n.Id == terminalId);
        return string.Equals(node?.Outcome, "failure", StringComparison.OrdinalIgnoreCase)
            ? DefModel.WorkflowOutcome.Failure
            : DefModel.WorkflowOutcome.Success;
    }

    /// <summary>
    /// Surfaces newly-produced step outputs from run state as <c>StepStarted</c>+<c>StepCompleted</c> events, in
    /// deterministic workflow order (design §8, Gap 2): step-output globals (<c>Global.&lt;stepId&gt;</c>) are ordered by
    /// the descriptor graph node sequence so the event log follows the pipeline regardless of checkpoint-file order.
    /// A sibling <c>Global.&lt;stepId&gt;__detail</c> string supplies the step's detail (Gap 1); <c>Global.assignedAgentId</c>
    /// records the dynamically assigned agent (F3); reserved terminal globals (<c>__terminalId</c>/<c>__message</c>/
    /// <c>__finalOutput</c>) are consumed by <see cref="ToCompleted"/> and never surfaced as steps.
    /// </summary>
    private async Task SurfaceOutputsAsync(
        RunContext run,
        DirectoryInfo checkpointDir,
        HashSet<string> reported,
        Func<MafRunEvent, Task> onEvent,
        CancellationToken ct)
    {
        var globals = await ReadGlobalsAsync(checkpointDir, ct).ConfigureAwait(false);

        // Assigned agent first so StepCompleted snapshots carry it.
        if (globals.TryGetValue("assignedAgentId", out var rawAgent) && reported.Add("assignedAgentId")
            && PowerFxConversion.FromJsonString(rawAgent) is JsonValue v && v.TryGetValue<string>(out var agentId) && !string.IsNullOrWhiteSpace(agentId))
        {
            run.AssignAgent(agentId);
        }

        var order = run.Workflow.Graph.Nodes.Select(n => n.Id).ToList();
        var stepNames = globals.Keys
            .Where(IsStepOutput)
            .OrderBy(name => OrderIndex(order, name));

        foreach (var name in stepNames)
        {
            if (!reported.Add(name))
            {
                continue;
            }

            var node = PowerFxConversion.FromJsonString(globals[name]);
            var detail = globals.TryGetValue($"{name}__detail", out var rawDetail)
                && PowerFxConversion.FromJsonString(rawDetail) is JsonValue dv && dv.TryGetValue<string>(out var d)
                ? d
                : null;

            run.SetOutput(name, node);
            await onEvent(new MafRunEvent.StepStarted(name)).ConfigureAwait(false);
            await onEvent(new MafRunEvent.StepCompleted(name, node, detail)).ConfigureAwait(false);
        }
    }

    /// <summary>True for a step-output global (not a reserved terminal global, a <c>__detail</c> sibling or the agent id).</summary>
    private static bool IsStepOutput(string name) =>
        name != "assignedAgentId"
        && !name.StartsWith("__", StringComparison.Ordinal)
        && !name.EndsWith("__detail", StringComparison.Ordinal);

    private static int OrderIndex(List<string> order, string name)
    {
        var i = order.IndexOf(name);
        return i < 0 ? int.MaxValue : i;
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
    /// Reads the stored value of a checkpointed <c>@Global</c> variable. PowerFx persists every value (records and
    /// their fields, tables and their cells) as a <c>{ "typeId": {...}, "value": &lt;serialized value&gt; }</c> envelope,
    /// recursively. This strips the envelopes at every level and returns the plain value as a JSON string (a string
    /// cell verbatim, anything else serialized) so <see cref="PowerFxConversion.FromJsonString"/> yields the plain node.
    /// </summary>
    internal static string ReadValueCell(JsonNode stored)
    {
        var unwrapped = Unwrap(stored);
        return unwrapped switch
        {
            null => "null",
            JsonValue jv when jv.TryGetValue<string>(out var s) => s,
            _ => unwrapped.ToJsonString(),
        };
    }

    /// <summary>Recursively removes the PowerFx <c>{ typeId, value }</c> envelopes so only plain JSON data remains.</summary>
    private static JsonNode? Unwrap(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count == 2 && obj.ContainsKey("typeId") && obj.ContainsKey("value"):
                return Unwrap(obj["value"]);
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    result[key] = Unwrap(value);
                }

                return result;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(Unwrap(item));
                }

                return items;
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// Describes a workflow/executor failure for the run's <c>Detail</c>. The declarative builder wraps an action's
    /// exception in a generic "Unhandled workflow failure" message, so the descriptive reason an expected failure
    /// carries (e.g. a module tool's <see cref="Flowgent.Runtime.Execution.StepFailedException"/> "Module '...' is
    /// unavailable") lives in an inner exception. This prefers the innermost <see cref="Flowgent.Runtime.Execution.StepFailedException"/>
    /// message when present, otherwise the deepest exception message; non-exception data falls back to its string.
    /// </summary>
    private static string? DescribeError(object? data)
    {
        if (data is not Exception ex)
        {
            return data?.ToString();
        }

        Exception? deepest = ex;
        for (var inner = ex; inner is not null; inner = inner.InnerException)
        {
            deepest = inner;
            if (inner is Flowgent.Runtime.Execution.StepFailedException stepFailure)
            {
                return stepFailure.Message;
            }
        }

        return deepest?.Message ?? ex.Message;
    }

    private static (string StepId, string? Prompt) DescribeRequest(ExternalRequest request)
    {
        // The declarative Question action's port id is "<actionId>_Input"; the logical step is the action id, so the
        // "_Input" suffix is stripped to keep StepStarted/AwaitingInput/InputReceived on the authored step id.
        var stepId = NormalizeStepId(request.PortInfo?.PortId) ?? "approval";
        if (request.Data.Is<DeclEvents.ExternalInputRequest>(out var input) && input is not null)
        {
            return (stepId, input.AgentResponse?.Text);
        }

        return (stepId, null);
    }

    private static string? NormalizeStepId(string? portId) =>
        portId is not null && portId.EndsWith("_Input", StringComparison.Ordinal)
            ? portId[..^"_Input".Length]
            : portId;

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
