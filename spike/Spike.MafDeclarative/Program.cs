using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Spike.MafDeclarative;
using Decl = Microsoft.Agents.AI.Workflows.Declarative;
using DeclEvents = Microsoft.Agents.AI.Workflows.Declarative.Events;

// ---------------------------------------------------------------------------
// Spike: can Microsoft Agent Framework DECLARATIVE workflows replace the custom
// YAML engine, driven by OUR OWN non-Foundry LLM and extended via MCP + HITL?
//
// Runs three demos in sequence, fully offline/deterministic:
//   (A) existing end-to-end demo: YAML from file + non-Foundry agent + AGENT-LEVEL MCP.
//   (F4) deterministic MCP tool via the DECLARATIVE InvokeMcpTool action + IMcpToolHandler
//        (NO LLM decides anything).
//   (F5) human-in-the-loop via the declarative RequestExternalInput action, with the
//        host supplying a scripted response and the workflow branching on it.
// ---------------------------------------------------------------------------

Console.WriteLine("=== Spike: MAF Declarative + our IChatClient + MCP + HITL (Via 2) ===");
Console.WriteLine();

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
var ct = cts.Token;

// One in-process MCP server shared by demo A (agent-level) and F4 (declarative-level).
Console.WriteLine("[setup] starting in-process MCP server (offline, in-memory pipe transport)...");
await using var mcp = await McpWordCountServer.StartAsync(ct);
var mcpTools = await mcp.GetToolsAsync(ct);
Console.WriteLine();

var okA = await Demos.RunExistingAsync(mcp, mcpTools, ct);
var okF4 = await Demos.RunF4McpHandlerAsync(mcp, ct);
var okF5 = await Demos.RunF5HitlAsync(ct);
var allOk = okA && okF4 && okF5;

Console.WriteLine();
Console.WriteLine($"[summary] A={okA} F4={okF4} F5={okF5}");
Console.WriteLine(allOk ? "=== Spike complete (all demos passed) ===" : "=== Spike finished WITH FAILURES ===");
return allOk ? 0 : 1;

internal static class Demos
{
    private static string WorkflowPath(string file) => Path.Combine(AppContext.BaseDirectory, file);

    // -----------------------------------------------------------------------
    // (A) Existing demo: YAML from file + non-Foundry agent + AGENT-LEVEL MCP.
    // -----------------------------------------------------------------------
    public static async Task<bool> RunExistingAsync(
        McpWordCountServer mcp, IReadOnlyList<AIFunction> mcpTools, CancellationToken ct)
    {
        Header("DEMO A", "existing end-to-end: YAML file + non-Foundry agent + agent-level MCP");

        var file = WorkflowPath("workflow.yaml");
        if (!File.Exists(file)) { Console.Error.WriteLine($"FATAL: {file} not found"); return false; }

        IChatClient fakeLlm = new FakeChatClient();
        var agentProvider = new SpikeAgentProvider(fakeLlm, mcpTools);
        var options = new DeclarativeWorkflowOptions(agentProvider);

        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: text => new ChatMessage(ChatRole.User, text));

        const string userInput = "Microsoft Agent Framework declarative workflows look promising";
        Console.WriteLine($"[run] input: \"{userInput}\"");
        Console.WriteLine("----- streamed events -----");

        string? final = null;
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userInput, cancellationToken: ct);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            if (evt is Decl.MessageActivityEvent activity)
            {
                final = activity.Message;
                Console.WriteLine($"  [MessageActivityEvent] {final}");
            }
            else if (evt is WorkflowOutputEvent output)
            {
                Console.WriteLine($"  [WorkflowOutputEvent] {output.Data}");
            }
        }

        Console.WriteLine("----- end -----");
        Console.WriteLine($"[A final] {final ?? "(none)"}");
        return final is not null;
    }

    // -----------------------------------------------------------------------
    // (F4) Deterministic MCP tool via the DECLARATIVE InvokeMcpTool action.
    // NO agent, NO LLM. The action routes to SpikeMcpToolHandler.InvokeToolAsync.
    // -----------------------------------------------------------------------
    public static async Task<bool> RunF4McpHandlerAsync(McpWordCountServer mcp, CancellationToken ct)
    {
        Header("DEMO F4", "deterministic MCP via declarative InvokeMcpTool + IMcpToolHandler (NO LLM)");

        var file = WorkflowPath("workflow.mcp.yaml");
        if (!File.Exists(file)) { Console.Error.WriteLine($"FATAL: {file} not found"); return false; }

        // No agent turn happens, but DeclarativeWorkflowOptions still needs an agent
        // provider. Our provider resolves nothing here (no InvokeAzureAgent action).
        var agentProvider = new SpikeAgentProvider(new FakeChatClient(), []);
        var options = new DeclarativeWorkflowOptions(agentProvider)
        {
            // THE key wiring for F4: the declarative MCP handler.
            McpToolHandler = new SpikeMcpToolHandler(mcp),
        };

        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: text => new ChatMessage(ChatRole.User, text));

        Console.WriteLine("[run] no LLM involved; InvokeMcpTool -> IMcpToolHandler.InvokeToolAsync");
        Console.WriteLine("----- streamed events -----");

        string? final = null;
        var sawHandler = false;
        StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow, "trigger", cancellationToken: ct);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case Decl.MessageActivityEvent activity:
                    final = activity.Message;
                    Console.WriteLine($"  [MessageActivityEvent] {final}");
                    break;
                case WorkflowOutputEvent output:
                    Console.WriteLine($"  [WorkflowOutputEvent] {output.Data}");
                    break;
                case WorkflowErrorEvent err:
                    Console.WriteLine($"  [WorkflowErrorEvent] {err.Data}");
                    break;
                default:
                    Console.WriteLine($"  [{evt.GetType().Name}] {evt.Data}");
                    break;
            }
        }

        // The handler printed its own evidence lines; the deterministic result also
        // flows into the final activity text ("= 5").
        sawHandler = final?.Contains("5 words", StringComparison.Ordinal) == true;
        Console.WriteLine("----- end -----");
        Console.WriteLine($"[F4 final] {final ?? "(none)"}");
        return final is not null && sawHandler;
    }

    // -----------------------------------------------------------------------
    // (F5) Human-in-the-loop via declarative RequestExternalInput.
    // The host drives both an APPROVED and a REJECTED run, supplying a scripted
    // ExternalResponse when the external-input request surfaces.
    // -----------------------------------------------------------------------
    public static async Task<bool> RunF5HitlAsync(CancellationToken ct)
    {
        Header("DEMO F5", "human-in-the-loop via declarative Question + branch (scripted response)");

        // Uses the declarative Question action (NOT RequestExternalInput) and ConditionGroup
        // (NOT `If`): see README finding F5 for why both substitutions are required in MAF
        // 1.27.0. The request surfaces with its prompt, we supply a scripted response, the
        // captured value reaches the next actions, and the correct branch is taken. We drive
        // BOTH an approved and a rejected run to show both branches.
        var file = WorkflowPath("workflow.hitl.question.yaml");
        if (!File.Exists(file)) { Console.Error.WriteLine($"FATAL: {file} not found"); return false; }

        var approvedOk = await RunHitlOnceAsync(file, "approved", "APPROVED branch taken", ct);
        var rejectedOk = await RunHitlOnceAsync(file, "rejected", "REJECTED branch taken", ct);
        return approvedOk && rejectedOk;
    }

    private static async Task<bool> RunHitlOnceAsync(
        string file, string scriptedAnswer, string expectBranch, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"--- HITL run: scripted answer = \"{scriptedAnswer}\" (expect: {expectBranch}) ---");

        var agentProvider = new SpikeAgentProvider(new FakeChatClient(), []);
        var options = new DeclarativeWorkflowOptions(agentProvider);
        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: text => new ChatMessage(ChatRole.User, text));

        string? final = null;
        var sawRequest = false;
        var responded = false;
        var capturedValueSeen = false;

        StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow, "start-approval", cancellationToken: ct);

        // Single blocking watch: it pumps the run, surfaces the pending external-input
        // request, lets us answer inline, and continues as far as the engine advances.
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case RequestInfoEvent reqInfo:
                    sawRequest = true;
                    Console.WriteLine($"  [RequestInfoEvent] external input requested; prompt: {DescribeRequest(reqInfo.Request)}");
                    Console.WriteLine($"  [host] supplying scripted response: \"{scriptedAnswer}\"");
                    // The declarative HITL port expects an ExternalInputResponse (a chat
                    // message), NOT a raw string.
                    var answer = new DeclEvents.ExternalInputResponse(
                        new ChatMessage(ChatRole.User, scriptedAnswer));
                    await run.SendResponseAsync(reqInfo.Request.CreateResponse(answer));
                    responded = true;
                    break;

                case Decl.MessageActivityEvent activity:
                    final = activity.Message;
                    Console.WriteLine($"  [MessageActivityEvent] {final}");
                    if (final?.Contains(scriptedAnswer, StringComparison.Ordinal) == true)
                    {
                        capturedValueSeen = true; // the gate's captured answer reached the next action
                    }
                    break;

                case WorkflowErrorEvent err:
                    Console.WriteLine($"  [WorkflowErrorEvent] {err.Data}");
                    break;
            }
        }

        var status = await run.GetStatusAsync(ct);
        var branchTaken = final?.Contains(expectBranch, StringComparison.Ordinal) == true;

        Console.WriteLine();
        Console.WriteLine($"  [result] requestSurfaced={sawRequest} responded={responded} " +
                          $"capturedValueReachedNextAction={capturedValueSeen} branchTaken={branchTaken} finalStatus={status}");

        return sawRequest && responded && capturedValueSeen && branchTaken;
    }

    private static string DescribeRequest(ExternalRequest request)
    {
        // The declarative HITL request payload is an ExternalInputRequest whose
        // AgentResponse carries the prompt text.
        if (request.Data.Is<DeclEvents.ExternalInputRequest>(out var inputRequest) && inputRequest is not null)
        {
            return $"\"{inputRequest.AgentResponse?.Text}\"";
        }

        return $"(payload type {request.Data.TypeId})";
    }

    private static void Header(string tag, string desc)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 78));
        Console.WriteLine($"{tag}: {desc}");
        Console.WriteLine(new string('=', 78));
    }
}
