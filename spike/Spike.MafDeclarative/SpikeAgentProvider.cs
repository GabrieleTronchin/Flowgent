using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;

namespace Spike.MafDeclarative;

/// <summary>
/// GOAL 2: the custom, non-Foundry agent provider.
///
/// The declarative workflow's <c>InvokeAzureAgent</c> action asks this provider to
/// resolve an agent by name. Instead of talking to Azure AI Foundry, we build a
/// <see cref="ChatClientAgent"/> on our OWN fake <see cref="IChatClient"/> and give it
/// the MCP tools (GOAL 3) so the agent turn calls the MCP wordCount tool. We then
/// simply forward the agent's streaming updates, which are already
/// <see cref="AgentResponseUpdate"/> - the exact type the declarative engine expects.
///
/// The conversation/message members are implemented with a tiny in-memory store so the
/// engine can create a conversation and append messages, fully offline.
/// </summary>
public sealed class SpikeAgentProvider : ResponseAgentProvider
{
    private readonly IChatClient _chatClient;
    private readonly IReadOnlyList<AITool> _tools;
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public SpikeAgentProvider(IChatClient chatClient, IEnumerable<AIFunction> mcpTools)
    {
        _chatClient = chatClient;
        var toolList = mcpTools.Cast<AITool>().ToList();
        _tools = toolList;

        // Expose the MCP tools to the declarative engine as well (it reads Functions
        // when deciding which tools an agent may call).
        Functions = toolList.OfType<AIFunction>().ToList();
        AllowMultipleToolCalls = true;
    }

    public override async IAsyncEnumerable<AgentResponseUpdate> InvokeAgentAsync(
        string agentId,
        string? agentVersion,
        string? conversationId,
        IEnumerable<ChatMessage>? messages,
        IDictionary<string, object?>? inputArguments,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[SpikeAgentProvider] resolving agentId='{agentId}' (non-Foundry) over fake IChatClient");

        // Build a ChatClientAgent on OUR chat client, with the MCP tools wired in so
        // function calling can route to the MCP server. AsAIAgent adds the
        // function-invoking middleware so tool calls are executed automatically.
        AIAgent agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = agentId,
            ChatOptions = new ChatOptions
            {
                Instructions = "You are the spike agent. Use the wordCount tool to count words, then answer.",
                Tools = _tools.ToList(),
            },
        });

        var inputMessages = (messages ?? []).ToList();
        if (conversationId is not null && _conversations.TryGetValue(conversationId, out var history))
        {
            // Prepend prior conversation history so the turn has context.
            inputMessages = [.. history, .. inputMessages];
        }

        await foreach (var update in agent.RunStreamingAsync(inputMessages, cancellationToken: cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public override Task<string> CreateConversationAsync(CancellationToken cancellationToken = default)
    {
        var id = $"conv-{Guid.NewGuid():N}";
        _conversations[id] = [];
        Console.WriteLine($"[SpikeAgentProvider] created conversation '{id}'");
        return Task.FromResult(id);
    }

    public override Task<ChatMessage> CreateMessageAsync(
        string conversationId,
        ChatMessage conversationMessage,
        CancellationToken cancellationToken = default)
    {
        var list = _conversations.GetOrAdd(conversationId, _ => []);
        if (conversationMessage.MessageId is null or "")
        {
            conversationMessage.MessageId = $"msg-{Guid.NewGuid():N}";
        }

        list.Add(conversationMessage);
        return Task.FromResult(conversationMessage);
    }

    public override Task<ChatMessage> GetMessageAsync(
        string conversationId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var message = _conversations.TryGetValue(conversationId, out var list)
            ? list.FirstOrDefault(m => m.MessageId == messageId)
            : null;
        return Task.FromResult(message ?? new ChatMessage(ChatRole.Assistant, string.Empty));
    }

    public override async IAsyncEnumerable<ChatMessage> GetMessagesAsync(
        string conversationId,
        int? limit = null,
        string? after = null,
        string? before = null,
        bool newestFirst = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var list))
        {
            yield break;
        }

        IEnumerable<ChatMessage> seq = newestFirst ? Enumerable.Reverse(list) : list;
        if (limit is > 0)
        {
            seq = seq.Take(limit.Value);
        }

        foreach (var message in seq)
        {
            yield return message;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
