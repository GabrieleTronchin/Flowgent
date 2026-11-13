using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Decl = Microsoft.Agents.AI.Workflows.Declarative;

namespace Spike.MafDeclarative;

/// <summary>
/// FEAT-004 syntax probe (reference-only, throwaway): proves the exact declarative action syntax the migrated
/// support workflow needs before it is committed to the module YAMLs — reading structured intake input in PowerFx
/// via the PROBE-A echo-record mechanism (no ParseJSON), Switch, ConditionGroup + GotoAction, Text(x,"0") number
/// formatting, and the Global.&lt;step&gt;/Global.&lt;step&gt;__detail convention the seam reads.
/// Run with: dotnet run -- feat004
/// </summary>
internal static class Feat004Probe
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        Console.WriteLine("=== FEAT-004 declarative-syntax probe ===");

        var file = Path.Combine(AppContext.BaseDirectory, "workflow.feat004.yaml");
        var options = new DeclarativeWorkflowOptions(new SpikeAgentProvider(new FakeChatClient(), []))
        {
            McpToolHandler = new ProbeMcpToolHandler(),
        };
        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: t => new ChatMessage(ChatRole.User, t));

        // Intake JSON string, exactly as MafWorkflowFactory.MapInput would serialize { subject, body }.
        const string input = "{\"subject\":\"Crash in Application A\",\"body\":\"NullReferenceException in OrderController\"}";

        string? final = null;
        string? error = null;
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input, cancellationToken: ct);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case Decl.MessageActivityEvent act:
                    final = act.Message;
                    Console.WriteLine($"  [MessageActivityEvent] {final}");
                    break;
                case WorkflowErrorEvent err:
                    error = err.Data?.ToString();
                    Console.WriteLine($"  [WorkflowErrorEvent] {error}");
                    break;
                case WorkflowOutputEvent o:
                    Console.WriteLine($"  [WorkflowOutputEvent] {o.Data}");
                    break;
            }
        }

        var pass = error is null && final is not null
            && final.Contains("route=TeamA", StringComparison.Ordinal)
            && final.Contains("agent=TeamA_BackendDeveloper", StringComparison.Ordinal)
            && final.Contains("detail=BackendBug (92%)", StringComparison.Ordinal);

        Console.WriteLine($"[FEAT-004 probe] pass={pass}");
        return pass ? 0 : 1;
    }
}
