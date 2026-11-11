using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Decl = Microsoft.Agents.AI.Workflows.Declarative;

namespace Spike.MafDeclarative;

/// <summary>
/// FEAT-004 agent probe (reference-only): logs the <c>inputArguments</c> an <c>InvokeAzureAgent</c> action passes to
/// the provider (so the migration knows how to carry <c>stepId</c>), and confirms the structured agent answer is
/// captured into a <c>Local</c> variable and surfaced via <c>MessageText</c>. Run with: dotnet run -- agentprobe
/// </summary>
internal static class AgentProbe
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        Console.WriteLine("=== FEAT-004 agent probe ===");
        var file = Path.Combine(AppContext.BaseDirectory, "workflow.agentprobe.yaml");
        var options = new DeclarativeWorkflowOptions(new LoggingAgentProvider());
        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(
            file, options, inputTransform: t => new ChatMessage(ChatRole.User, t));

        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, "Tell a joke about: cats", cancellationToken: ct);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case Decl.MessageActivityEvent act:
                    Console.WriteLine($"  [MessageActivityEvent] {act.Message}");
                    break;
                case WorkflowErrorEvent err:
                    Console.WriteLine($"  [WorkflowErrorEvent] {err.Data}");
                    break;
            }
        }

        return 0;
    }

    private sealed class LoggingAgentProvider : ResponseAgentProvider
    {
        public override async IAsyncEnumerable<AgentResponseUpdate> InvokeAgentAsync(
            string agentId, string? agentVersion, string? conversationId,
            IEnumerable<ChatMessage>? messages, IDictionary<string, object?>? inputArguments,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var args = inputArguments is null ? "(null)" : string.Join(", ", inputArguments.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            var msgs = string.Join(" | ", (messages ?? []).Select(m => $"{m.Role}:{m.Text}"));
            Console.WriteLine($"  [provider] agentId='{agentId}' inputArguments=[{args}] messages=[{msgs}]");
            yield return new AgentResponseUpdate(ChatRole.Assistant, "{\"joke\":\"[Demo] Why did the cats cross the road?\"}");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public override Task<string> CreateConversationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"conv-{Guid.NewGuid():N}");

        public override Task<ChatMessage> CreateMessageAsync(string conversationId, ChatMessage conversationMessage, CancellationToken cancellationToken = default) =>
            Task.FromResult(conversationMessage);

        public override Task<ChatMessage> GetMessageAsync(string conversationId, string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatMessage(ChatRole.Assistant, string.Empty));

        public override async IAsyncEnumerable<ChatMessage> GetMessagesAsync(
            string conversationId, int? limit = null, string? after = null, string? before = null, bool newestFirst = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
