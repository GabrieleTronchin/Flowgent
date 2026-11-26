namespace Flowgent.Engine.Service.Endpoints;

using Flowgent.Contracts.Api;
using Flowgent.Engine.Service.Endpoints.Primitives;
using Flowgent.Engine.Core.Agents;
using Flowgent.Engine.Core.Configuration;
using Flowgent.Engine.Core.Llm;
using Flowgent.Engine.Core.Runs;
using Microsoft.Extensions.Options;

/// <summary>Observability: transition events, agent status and LLM telemetry.</summary>
internal sealed class MonitoringEndpoints : IEndpoint
{
    private const int MaxEvents = 200;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EngineRoutes.Base).WithTags("Monitoring");

        group.MapGet("/events", ListEvents)
            .WithSummary("Run transition events (audit log), newest first, max 200");

        group.MapGet("/agents", ListAgents)
            .WithSummary("Agents of all modules with status (Idle/Working)");

        group.MapGet("/agents/{agentId}/telemetry", GetAgentTelemetry)
            .WithSummary("LLM telemetry of an agent");

        group.MapGet("/telemetry/summary", GetTelemetrySummary)
            .WithSummary("Global LLM usage statistics");
    }

    private static async Task<IResult> ListEvents(IRunStore store, int? limit, string? moduleId, string? workflowId, Guid? runId, CancellationToken ct) =>
        Results.Ok(await store.ListEventsAsync(new EventQuery(moduleId, workflowId, runId, Math.Clamp(limit ?? MaxEvents, 1, MaxEvents)), ct));

    private static async Task<IResult> ListAgents(AgentStatusService agents, IOptions<EngineOptions> options, string? moduleId, CancellationToken ct) =>
        options.Value.EnableVisualization
            ? Results.Ok(await agents.GetAgentsAsync(moduleId, ct))
            : ApiResults.NotFound("Visualization is disabled.");

    private static IResult GetAgentTelemetry(string agentId, LlmTelemetryStore store) =>
        Results.Ok(store.GetAgentTelemetry(agentId));

    private static IResult GetTelemetrySummary(LlmTelemetryStore store, string? moduleId) =>
        Results.Ok(store.GetSummary(moduleId));
}
