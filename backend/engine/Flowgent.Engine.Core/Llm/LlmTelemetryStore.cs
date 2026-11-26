namespace Flowgent.Engine.Core.Llm;

using System.Collections.Concurrent;
using Flowgent.Contracts.Api;
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
