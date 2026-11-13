namespace Flowgent.Runtime.Maf;

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Llm;

/// <summary>
/// Production <see cref="ResponseAgentProvider"/> over the engine's <see cref="IChatClient"/> (design §6), one instance
/// per run, patterned on <c>spike/Spike.MafDeclarative/SpikeAgentProvider.cs</c>. The declarative
/// <c>InvokeAzureAgent</c> action resolves an agent through this provider rather than Azure AI Foundry: it builds a
/// <see cref="ChatClientAgent"/> on the engine chat client,
/// attributes the call for telemetry, and — in Demo mode — attaches the <see cref="DemoResponseProvider"/> output.
/// Conversation/message storage is delegated to a pluggable <see cref="IConversationStore"/>.
/// </summary>
public sealed class MafAgentProvider : ResponseAgentProvider
{
    /// <summary>
    /// Separator in the declarative <c>agent.name</c> that carries both the logical step id and the static agent id as
    /// <c>"&lt;stepId&gt;|&lt;agentId&gt;"</c>. In MAF 1.27.0 <c>InvokeAzureAgent</c> does not surface the action id or any
    /// custom <c>inputs</c> to the provider (confirmed empirically), so <c>agent.name</c> is the only channel: the
    /// provider keys the Demo response and telemetry step id off the left part and resolves the agent off the right
    /// part (or, when absent, off the run's dynamically-assigned agent). A name with no separator is both the step id
    /// and the agent id.
    /// </summary>
    public const char NameSeparator = '|';

    private readonly RunContext _run;
    private readonly IChatClient _chatClient;
    private readonly LlmOptions _llmOptions;
    private readonly DemoResponseProvider _demo;
    private readonly IConversationStore _conversations;

    public MafAgentProvider(
        RunContext run,
        IChatClient chatClient,
        LlmOptions llmOptions,
        DemoResponseProvider demo,
        IConversationStore conversations)
    {
        _run = run;
        _chatClient = chatClient;
        _llmOptions = llmOptions;
        _demo = demo;
        _conversations = conversations;
        AllowMultipleToolCalls = true;
    }

    public override async IAsyncEnumerable<AgentResponseUpdate> InvokeAgentAsync(
        string agentId,
        string? agentVersion,
        string? conversationId,
        IEnumerable<ChatMessage>? messages,
        IDictionary<string, object?>? inputArguments,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var separator = agentId.IndexOf(NameSeparator, StringComparison.Ordinal);
        var stepId = separator >= 0 ? agentId[..separator] : agentId;
        var staticAgentId = separator >= 0 ? agentId[(separator + 1)..] : agentId;

        // Resolve the agent for telemetry: a static/assigned agent named in the YAML (the support workflow passes the
        // dynamically-assigned id via the name), else the run's already-assigned agent.
        var resolvedAgentId = _run.Module.FindAgent(staticAgentId) is not null ? staticAgentId : _run.AssignedAgentId;
        if (resolvedAgentId is not null)
        {
            _run.AssignAgent(resolvedAgentId);
        }

        var inputMessages = (messages ?? []).ToList();
        if (conversationId is not null)
        {
            inputMessages = [.. _conversations.List(conversationId), .. inputMessages];
        }

        var renderedPrompt = string.Join("\n", inputMessages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        var chatOptions = BuildChatOptions(stepId, resolvedAgentId, renderedPrompt);

        var agent = _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = resolvedAgentId ?? staticAgentId,
            ChatOptions = chatOptions,
        });

        // Use the non-streaming path so the call goes through IChatClient.GetResponseAsync, where LlmTelemetryMiddleware
        // records it (streaming would bypass telemetry). The single response is surfaced as one update.
        var response = await agent.RunAsync(inputMessages, cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToAgentResponseUpdates())
        {
            yield return update;
        }
    }

    private ChatOptions BuildChatOptions(string stepId, string? agentId, string renderedPrompt)
    {
        // The output JSON Schema is carried in the declarative YAML (not the manifest), so the response format is set
        // by the authored prompt; the engine validates the returned JSON downstream. Telemetry attribution and the Demo
        // response are attached here so LlmTelemetryMiddleware records the call and DemoChatClient returns the demo JSON.
        var options = LlmCallAttribution.ForAgent(new ChatOptions(), agentId, _run.Module.Id, _run.Workflow.Id, _run.RunId, stepId);

        if (_llmOptions.IsDemo)
        {
            var demo = _demo.Resolve(_run.Workflow.Id, stepId, renderedPrompt, _run);
            options = LlmCallAttribution.WithDemoResponse(options, demo.ToJsonString());
        }

        return options;
    }

    public override async Task<string> CreateConversationAsync(CancellationToken cancellationToken = default) =>
        await _conversations.CreateConversationAsync(cancellationToken).ConfigureAwait(false);

    public override async Task<ChatMessage> CreateMessageAsync(
        string conversationId,
        ChatMessage conversationMessage,
        CancellationToken cancellationToken = default) =>
        await _conversations.AppendAsync(conversationId, conversationMessage, cancellationToken).ConfigureAwait(false);

    public override async Task<ChatMessage> GetMessageAsync(
        string conversationId,
        string messageId,
        CancellationToken cancellationToken = default) =>
        await _conversations.GetAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false)
            ?? new ChatMessage(ChatRole.Assistant, string.Empty);

    public override async IAsyncEnumerable<ChatMessage> GetMessagesAsync(
        string conversationId,
        int? limit = null,
        string? after = null,
        string? before = null,
        bool newestFirst = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var message in _conversations.List(conversationId, limit, newestFirst))
        {
            yield return message;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
