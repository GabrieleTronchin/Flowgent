namespace Flowgent.Runtime.Llm;

using System.Collections.Concurrent;
using System.Diagnostics;
using Flowgent.Contracts.Api;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

/// <summary>In-memory store of LLM calls (Agents page: tokens, calls, latency).</summary>
public sealed class LlmTelemetryStore
{
    private readonly ConcurrentQueue<LlmCallInfo> _entries = new();

    public void Record(LlmCallInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Enqueue(entry);
    }

    public IReadOnlyList<LlmCallInfo> GetCalls(string? moduleId = null) =>
        _entries.Where(e => moduleId is null || e.ModuleId == moduleId).ToList();

    public AgentTelemetry GetAgentTelemetry(string agentId)
    {
        var entries = _entries.Where(e => e.AgentId == agentId).ToList();
        if (entries.Count == 0)
        {
            return new AgentTelemetry(agentId, 0, 0, 0, 0, null);
        }

        return new AgentTelemetry(
            agentId,
            entries.Sum(e => e.PromptTokens),
            entries.Sum(e => e.CompletionTokens),
            entries.Count,
            entries.Average(e => e.LatencyMs),
            entries.OrderByDescending(e => e.Timestamp).First());
    }

    public TelemetrySummary GetSummary(string? moduleId = null)
    {
        var entries = GetCalls(moduleId);
        if (entries.Count == 0)
        {
            return new TelemetrySummary(0, 0, 0, 0);
        }

        return new TelemetrySummary(
            entries.Sum(e => e.PromptTokens + e.CompletionTokens),
            entries.Count,
            entries.Average(e => e.LatencyMs),
            (double)entries.Count(e => !e.Success) / entries.Count);
    }
}

/// <summary>
/// Records every LLM call in <see cref="LlmTelemetryStore"/> and strips the engine-internal attribution keys
/// (<see cref="LlmCallAttribution"/>) so they are never forwarded to the provider.
/// </summary>
public sealed class LlmTelemetryMiddleware(
    IChatClient innerClient,
    ILogger<LlmTelemetryMiddleware> logger,
    LlmTelemetryStore telemetryStore) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var attribution = Attribution.From(options);
        var forwarded = Strip(options, keepDemo: true);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.GetResponseAsync(messages, forwarded, cancellationToken);
            stopwatch.Stop();

            var entry = attribution.Entry(
                response.ModelId ?? forwarded?.ModelId ?? "unknown",
                (int)(response.Usage?.InputTokenCount ?? 0),
                (int)(response.Usage?.OutputTokenCount ?? 0),
                stopwatch.ElapsedMilliseconds,
                success: true,
                error: null);
            telemetryStore.Record(entry);
            logger.LogInformation(
                "LLM call: Agent={Agent}, Step={Step}, Model={Model}, PromptTokens={Prompt}, CompletionTokens={Completion}, Latency={Latency}ms",
                attribution.AgentId ?? "-", attribution.StepId ?? "-", entry.ModelName, entry.PromptTokens, entry.CompletionTokens, entry.LatencyMs);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            telemetryStore.Record(attribution.Entry(forwarded?.ModelId ?? "unknown", 0, 0, stopwatch.ElapsedMilliseconds, success: false, error: ex.Message));
            logger.LogError(ex, "LLM call failed: Agent={Agent}, Step={Step}, Latency={Latency}ms", attribution.AgentId ?? "-", attribution.StepId ?? "-", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>Removes the attribution keys (and the demo response unless <paramref name="keepDemo"/>).</summary>
    internal static ChatOptions? Strip(ChatOptions? options, bool keepDemo)
    {
        if (options?.AdditionalProperties is not { } props
            || !LlmCallAttribution.AllKeys.Any(props.ContainsKey))
        {
            return options;
        }

        var forwarded = options.Clone();
        foreach (var key in LlmCallAttribution.AllKeys)
        {
            if (!(keepDemo && key == LlmCallAttribution.DemoResponseKey))
            {
                forwarded.AdditionalProperties!.Remove(key);
            }
        }

        if (forwarded.AdditionalProperties!.Count == 0)
        {
            forwarded.AdditionalProperties = null;
        }

        return forwarded;
    }

    private sealed record Attribution(string? AgentId, string? ModuleId, string? WorkflowId, Guid? RunId, string? StepId)
    {
        public static Attribution From(ChatOptions? options) => new(
            LlmCallAttribution.Get(options, LlmCallAttribution.AgentIdKey),
            LlmCallAttribution.Get(options, LlmCallAttribution.ModuleIdKey),
            LlmCallAttribution.Get(options, LlmCallAttribution.WorkflowIdKey),
            Guid.TryParse(LlmCallAttribution.Get(options, LlmCallAttribution.RunIdKey), out var runId) ? runId : null,
            LlmCallAttribution.Get(options, LlmCallAttribution.StepIdKey));

        public LlmCallInfo Entry(string model, int prompt, int completion, long latency, bool success, string? error) =>
            new(model, prompt, completion, latency, success, DateTimeOffset.UtcNow, error, AgentId, ModuleId, WorkflowId, RunId, StepId);
    }
}

/// <summary>
/// Removes the demo response key before a real provider is called (Demo mode keeps it for <see cref="DemoChatClient"/>).
/// </summary>
internal sealed class StripDemoResponseChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, LlmTelemetryMiddleware.Strip(options, keepDemo: false), cancellationToken);
}
