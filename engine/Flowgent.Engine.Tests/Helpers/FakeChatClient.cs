namespace Flowgent.Engine.Tests.Helpers;

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

/// <summary>Chat client returning scripted responses and recording what it receives.</summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Func<IReadOnlyList<ChatMessage>, ChatOptions?, string> _responder;

    public FakeChatClient(string response)
        : this((_, _) => response)
    {
    }

    public FakeChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, string> responder) => _responder = responder;

    public static FakeChatClient Throwing(Exception exception) => new((_, _) => throw exception);

    public ConcurrentQueue<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = new();

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Enqueue((list, options));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responder(list, options)))
        {
            ModelId = "fake-model",
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to dispose
    }
}
