namespace Flowgent.Engine.Core.Llm;

using System.Diagnostics;
using Flowgent.Contracts.Api;
using Microsoft.Extensions.AI;

/// <summary>
/// Removes the demo response key before a real provider is called (Demo mode keeps it for <see cref="DemoChatClient"/>).
/// </summary>
internal sealed class StripDemoResponseChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, LlmTelemetryMiddleware.Strip(options, keepDemo: false), cancellationToken);
}
