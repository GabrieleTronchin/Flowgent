namespace Flowgent.Contracts.Api;

public sealed record TelemetrySummary(int TotalTokens, int TotalCalls, double AverageLatencyMs, double ErrorRate);
