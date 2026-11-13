using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Decl = Microsoft.Agents.AI.Workflows.Declarative;

namespace Spike.MafDeclarative;

/// <summary>Probe: which action ends a branch so later actions do not run (EndConversation?). Run: dotnet run -- endprobe</summary>
internal static class EndProbe
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "workflow.endprobe.yaml");
        var options = new DeclarativeWorkflowOptions(new SpikeAgentProvider(new FakeChatClient(), []));
        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(file, options, t => new ChatMessage(ChatRole.User, t));
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, "x", cancellationToken: ct);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            if (evt is Decl.MessageActivityEvent a)
            {
                Console.WriteLine($"  [activity] {a.Message}");
            }
            else if (evt is WorkflowErrorEvent e)
            {
                Console.WriteLine($"  [error] {e.Data}");
            }
        }

        return 0;
    }
}
