namespace Flowgent.Runtime.Llm;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

/// <summary>
/// Offline <see cref="IChatClient"/> used when <c>Flowgent:Llm:Provider</c> is <c>Demo</c>. It returns the response
/// computed by the llm step from the workflow's <c>demo:</c> block (passed in <see cref="ChatOptions.AdditionalProperties"/>),
/// after a simulated latency and with estimated token usage, so pipelines, telemetry and the dashboard behave as with a real model.
/// </summary>
public sealed class DemoChatClient(TimeSpan latency) : IChatClient
{
    public const string DemoModelId = "demo-simulated";

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var json = LlmCallAttribution.Get(options, LlmCallAttribution.DemoResponseKey)
            ?? throw new InvalidOperationException("No demo response was provided for this call (Demo mode requires a demo: block on the llm step).");

        if (latency > TimeSpan.Zero)
        {
            await Task.Delay(latency, cancellationToken);
        }

        var prompt = string.Join("\n", messages.Select(m => m.Text));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
        {
            ModelId = DemoModelId,
            Usage = new UsageDetails
            {
                InputTokenCount = EstimateTokens(prompt),
                OutputTokenCount = EstimateTokens(json),
            },
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text) { ModelId = DemoModelId };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        // Nothing to dispose
    }

    // Rough approximation (~4 characters per token) for simulated usage telemetry.
    internal static long EstimateTokens(string text) => Math.Max(1, text.Length / 4);
}
