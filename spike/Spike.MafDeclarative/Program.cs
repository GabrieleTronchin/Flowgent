using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Spike.MafDeclarative;
using DeclarativeEvents = Microsoft.Agents.AI.Workflows.Declarative;

// ---------------------------------------------------------------------------
// Spike: can Microsoft Agent Framework DECLARATIVE workflows replace the custom
// YAML engine, driven by OUR OWN non-Foundry LLM and extended via MCP?
//
// This console app proves four things end to end, fully offline/deterministic:
//   1. Load a declarative workflow from a YAML FILE at runtime.
//   2. Drive a NON-FOUNDRY agent via a custom ResponseAgentProvider over a fake IChatClient.
//   3. Invoke a TOOL from an MCP server inside the workflow (agent-level MCP).
//   4. Run the workflow and print streamed events + final output.
// ---------------------------------------------------------------------------

Console.WriteLine("=== Spike: MAF Declarative + our IChatClient + MCP (Via 2) ===");
Console.WriteLine();

var workflowFile = Path.Combine(AppContext.BaseDirectory, "workflow.yaml");
if (!File.Exists(workflowFile))
{
    Console.Error.WriteLine($"FATAL: workflow.yaml not found next to the binary at {workflowFile}");
    return 1;
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var ct = cts.Token;

// --- GOAL 3 setup: start the in-process MCP server and read its tools. ---
Console.WriteLine("[step] starting in-process MCP server (offline, in-memory pipe transport)...");
await using var mcp = await McpWordCountServer.StartAsync(ct);
var mcpTools = await mcp.GetToolsAsync(ct);
Console.WriteLine();

// --- GOAL 2 setup: our fake IChatClient + custom provider that resolves agents to it. ---
Console.WriteLine("[step] wiring custom ResponseAgentProvider over fake IChatClient...");
IChatClient fakeLlm = new FakeChatClient();
var agentProvider = new SpikeAgentProvider(fakeLlm, mcpTools);

var options = new DeclarativeWorkflowOptions(agentProvider);
Console.WriteLine();

// --- GOAL 1: load the workflow FROM THE YAML FILE at runtime. ---
Console.WriteLine($"[step] loading declarative workflow from file: {workflowFile}");
Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
    workflowFile,
    options,
    inputTransform: text => new ChatMessage(ChatRole.User, text));
Console.WriteLine("[ok] workflow object built from YAML file.");
Console.WriteLine();

// --- GOAL 4: run and observe. ---
const string userInput = "Microsoft Agent Framework declarative workflows look promising";
Console.WriteLine($"[step] running workflow with input: \"{userInput}\"");
Console.WriteLine("----- streamed workflow events -----");

string? finalOutput = null;
StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, userInput, cancellationToken: ct);

await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
{
    switch (evt)
    {
        case AgentResponseUpdateEvent agentUpdate:
            var text = agentUpdate.Update?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                Console.WriteLine($"  [AgentRunUpdateEvent] {text}");
            }
            break;

        case DeclarativeEvents.MessageActivityEvent activity:
            // This is the evaluated SendActivity text - the workflow's final output.
            finalOutput = activity.Message;
            Console.WriteLine($"  [MessageActivityEvent] {finalOutput}");
            break;

        case WorkflowOutputEvent output:
            Console.WriteLine($"  [WorkflowOutputEvent] {output.Data}");
            break;

        case ExecutorCompletedEvent done:
            Console.WriteLine($"  [ExecutorCompletedEvent] executor={done.ExecutorId}");
            break;

        default:
            Console.WriteLine($"  [{evt.GetType().Name}]");
            break;
    }
}

Console.WriteLine("----- end of stream -----");
Console.WriteLine();
Console.WriteLine($"[final output] {finalOutput ?? "(none captured)"}");
Console.WriteLine();
Console.WriteLine("=== Spike complete ===");
return 0;
