using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Spike.MafDeclarative;

/// <summary>
/// GOAL 2: our OWN non-Foundry LLM. A deterministic, offline fake IChatClient.
/// No API key, no network, no Azure/Foundry. It mimics a tool-calling model:
///
///   - Turn 1: given the user message, it asks to call the MCP-backed "wordCount"
///     tool (emits a FunctionCallContent). The Agent Framework executes the tool
///     (which routes to our in-process MCP server) and calls us again with the
///     tool result.
///   - Turn 2: seeing the tool result, it produces a final deterministic answer.
///
/// This is how we prove the custom IChatClient is really the LLM driving the run
/// AND that an MCP tool is actually invoked mid-turn.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private const string WordCountToolName = "wordCount";

    public ChatClientMetadata Metadata { get; } = new("spike-fake-demo");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();

        // If the conversation already contains a tool result for wordCount, we are
        // on the second turn: produce the final answer using that result.
        var toolResult = FindToolResult(messageList);
        if (toolResult is not null)
        {
            Console.WriteLine($"[FakeChatClient] saw MCP tool result -> composing final answer");
            var answer = $"[fake-llm] The MCP wordCount tool reported {toolResult} word(s) in your message.";
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
            {
                ResponseId = Guid.NewGuid().ToString("N"),
                ModelId = Metadata.DefaultModelId,
                FinishReason = ChatFinishReason.Stop,
            };
        }

        // First turn: if the wordCount tool is available, request it.
        if (ToolAvailable(options, WordCountToolName))
        {
            var userText = messageList.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
            Console.WriteLine($"[FakeChatClient] requesting MCP tool call '{WordCountToolName}' for input: \"{userText}\"");
            var call = new FunctionCallContent(
                callId: "call-1",
                name: WordCountToolName,
                arguments: new Dictionary<string, object?> { ["text"] = userText });

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, [call]))
            {
                ResponseId = Guid.NewGuid().ToString("N"),
                ModelId = Metadata.DefaultModelId,
                FinishReason = ChatFinishReason.ToolCalls,
            };
        }

        // Fallback (no tools): echo deterministically.
        var fallback = "[fake-llm] no tools available; echoing input.";
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, fallback))
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Derive the streaming form from the non-streaming response so behavior is identical.
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private static bool ToolAvailable(ChatOptions? options, string toolName) =>
        options?.Tools?.Any(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>Scan the conversation for a prior wordCount tool result and extract the count.</summary>
    private static int? FindToolResult(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent result &&
                    string.Equals(result.CallId, "call-1", StringComparison.Ordinal))
                {
                    return ExtractCount(result.Result);
                }
            }
        }

        return null;
    }

    private static int? ExtractCount(object? result)
    {
        switch (result)
        {
            case null:
                return null;
            case int i:
                return i;
            case long l:
                return (int)l;
            case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt32(out var n):
                return n;
        }

        // The MCP tool result commonly arrives as text (possibly JSON-wrapped).
        var text = result.ToString() ?? string.Empty;
        // Try to pull the first integer out of the text content.
        var match = System.Text.RegularExpressions.Regex.Match(text, @"-?\d+");
        return match.Success && int.TryParse(match.Value, out var parsed) ? parsed : null;
    }
}
