namespace Flowgent.Engine.Service.Endpoints;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Schemas;
using Flowgent.Runtime.Agents;
using Flowgent.Runtime.Configuration;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.HumanInput;
using Flowgent.Runtime.Intake;
using Flowgent.Runtime.Llm;
using Flowgent.Runtime.Modules;
using Flowgent.Runtime.Runs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>Module-agnostic engine API under <c>/api/engine</c>.</summary>
public static class EngineEndpoints
{
    public const int MaxEvents = 200;

    public static IEndpointRouteBuilder MapEngineEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(EngineRoutes.Base);

        MapCatalog(api.MapGroup("").WithTags("Modules and workflows"));
        MapRuns(api.MapGroup("").WithTags("Runs"));
        MapHumanInputs(api.MapGroup("/human-inputs").WithTags("Human input"));
        MapMonitoring(api.MapGroup("").WithTags("Monitoring"));

        return app;
    }

    private static void MapCatalog(RouteGroupBuilder group)
    {
        group.MapGet("/config", (IOptions<EngineOptions> engine, IOptions<LlmOptions> llm) =>
            Results.Ok(BuildConfig(engine.Value, llm.Value)))
            .WithSummary("Runtime configuration for the dashboard (never secrets)");

        group.MapGet("/modules", (IModuleCatalog catalog) => Results.Ok(catalog.Modules.Select(m => m.ToInfo())))
            .WithSummary("Modules with availability and validation errors");

        group.MapGet("/workflows", (IModuleCatalog catalog, string? moduleId) =>
            Results.Ok(catalog.Modules
                .Where(m => m.Definition is not null && (moduleId is null || m.Id == moduleId))
                .SelectMany(m => m.Definition!.Workflows.Select(w => ToWorkflowInfo(m, w)))))
            .WithSummary("Workflows with graph, input schema and samples");

        group.MapGet("/workflows/{workflowId}", (string workflowId, IModuleCatalog catalog) =>
            catalog.FindWorkflow(workflowId) is { } handle
                ? Results.Ok(ToWorkflowInfo(handle.Module, handle.Workflow))
                : NotFound($"Workflow '{workflowId}' not found."))
            .WithSummary("A workflow definition");

        group.MapGet("/schemas/{name}", (string name) =>
            FormatSchemas.GetText(name) is { } text
                ? Results.Text(text, "application/schema+json")
                : NotFound($"Schema '{name}' not found. Available: {string.Join(", ", FormatSchemas.Names)}."))
            .WithSummary("JSON Schemas of the YAML formats (module, workflow, samples)");
    }

    private static void MapRuns(RouteGroupBuilder group)
    {
        group.MapPost("/workflows/{workflowId}/runs", async (string workflowId, [FromBody] JsonNode? input, IntakeService intake, CancellationToken ct) =>
            await intake.SubmitAsync(workflowId, input, ct) switch
            {
                IntakeResult.Accepted accepted => Results.Accepted(EngineRoutes.Run(accepted.RunId), new IntakeAccepted(accepted.MessageId, accepted.RunId)),
                IntakeResult.Invalid invalid => Results.Problem(
                    title: "Invalid input",
                    detail: invalid.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errors"] = invalid.Errors.Select(e => new { pointer = e.Pointer, message = e.Message }).ToList(),
                    }),
                IntakeResult.NotFound notFound => NotFound(notFound.Message),
                IntakeResult.Unavailable unavailable => Results.Problem(
                    title: "Module unavailable", detail: unavailable.Message, statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            })
            .WithSummary("Submit an input to a workflow (async: saved to the inbox, returns 202)");

        group.MapGet("/runs", async (IRunStore store, string? moduleId, string? workflowId, RunStatus? status, int? limit, CancellationToken ct) =>
            Results.Ok((await store.ListAsync(new RunQuery(moduleId, workflowId, status, Math.Clamp(limit ?? 500, 1, 1000)), ct)).Select(r => r.ToSummary())))
            .WithSummary("Runs, newest first, filterable by module, workflow and status");

        group.MapGet("/runs/{runId:guid}", async (Guid runId, IRunStore store, CancellationToken ct) =>
            await store.GetAsync(runId, ct) is { } run ? Results.Ok(run.ToDetail()) : NotFound($"Run {runId} not found."))
            .WithSummary("A run with input, step outputs and final output");

        group.MapPost("/runs/{runId:guid}/abort", async (Guid runId, IRunStore store, RunRegistry registry, CancellationToken ct) =>
        {
            // Running workflow: cancel it. The orchestrator records the single Failed transition
            // (this also releases a pending human input).
            if (registry.TryAbort(runId))
            {
                return Results.Accepted();
            }

            var run = await store.GetAsync(runId, ct);
            if (run is null)
            {
                return NotFound($"Run {runId} not found.");
            }

            if (run.Status.IsTerminal())
            {
                return Conflict($"Run {runId} has already completed ({run.Status}).");
            }

            // Queued (or orphaned) run: end it here; the inbox processor skips terminal runs.
            await store.RecordAsync(new RunTransition(runId, RunEventKind.Failed, RunStatus.Failed, run.CurrentStepId, RunRegistry.AbortReason), ct);
            return Results.NoContent();
        })
        .WithSummary("Abort a run; it ends in Failed state with reason 'Aborted by user'");
    }

    private static void MapHumanInputs(RouteGroupBuilder group)
    {
        group.MapGet("", (HumanInputRegistry registry, string? moduleId, string? workflowId) =>
            Results.Ok(registry.GetPending(moduleId, workflowId).Select(p => p.ToInfo())))
            .WithSummary("Runs awaiting human input, with their review payload");

        group.MapPost("/{runId:guid}/approve", async (Guid runId, HumanInputService service, CancellationToken ct) =>
            ToResult(await service.ApproveAsync(runId, ct)))
            .WithSummary("Approve a run awaiting input");

        group.MapPost("/{runId:guid}/reject", async (Guid runId, RejectRequest? request, HumanInputService service, CancellationToken ct) =>
            ToResult(await service.RejectAsync(runId, request?.Reason, ct)))
            .WithSummary("Reject a run awaiting input, with an optional reason");
    }

    private static void MapMonitoring(RouteGroupBuilder group)
    {
        group.MapGet("/events", async (IRunStore store, int? limit, string? moduleId, string? workflowId, Guid? runId, CancellationToken ct) =>
            Results.Ok(await store.ListEventsAsync(new EventQuery(moduleId, workflowId, runId, Math.Clamp(limit ?? MaxEvents, 1, MaxEvents)), ct)))
            .WithSummary("Run transition events (audit log), newest first, max 200");

        group.MapGet("/agents", async (AgentStatusService agents, IOptions<EngineOptions> options, string? moduleId, CancellationToken ct) =>
            options.Value.EnableVisualization
                ? Results.Ok(await agents.GetAgentsAsync(moduleId, ct))
                : NotFound("Visualization is disabled."))
            .WithSummary("Agents of all modules with status (Idle/Working)");

        group.MapGet("/agents/{agentId}/telemetry", (string agentId, LlmTelemetryStore store) => Results.Ok(store.GetAgentTelemetry(agentId)))
            .WithSummary("LLM telemetry of an agent");

        group.MapGet("/telemetry/summary", (LlmTelemetryStore store, string? moduleId) => Results.Ok(store.GetSummary(moduleId)))
            .WithSummary("Global LLM usage statistics");

        group.MapGet("/inbox", async (IInboxStore inbox, string? status, string? moduleId, CancellationToken ct) =>
            Results.Ok(await inbox.ListAsync(status, moduleId, ct)))
            .WithSummary("Inbox messages (queued, processed, failed)");

        group.MapGet("/inbox/{messageId:guid}", async (Guid messageId, IInboxStore inbox, CancellationToken ct) =>
            (await inbox.ListAsync(null, null, ct)).FirstOrDefault(m => m.Id == messageId) is { } message
                ? Results.Ok(message)
                : NotFound($"Inbox message {messageId} not found."))
            .WithSummary("An inbox message");
    }

    /// <summary>Dashboard view of the runtime configuration: never the API key or the endpoint URL.</summary>
    public static RuntimeConfigInfo BuildConfig(EngineOptions engine, LlmOptions llm) =>
        new(engine.SequentialProcessing, llm.IsDemo, ChatClientSetup.EffectiveProvider(llm), ChatClientSetup.EffectiveModel(llm));

    internal static WorkflowInfo ToWorkflowInfo(ModuleState module, WorkflowDescriptor workflow)
    {
        var definition = module.Definition!;
        return new WorkflowInfo(
            workflow.Id,
            module.Id,
            workflow.Name,
            workflow.Description,
            workflow.Version,
            module.IsAvailable,
            definition.GetSchemaNode(workflow.InputSchema).DeepClone(),
            BuildGraph(workflow),
            definition.Samples
                .Where(s => s.Workflow == workflow.Id)
                .Select(s => new SampleInfo(s.Id, s.Name, s.Group, s.Description, s.Workflow, s.Input?.DeepClone(), s.ExpectInvalid))
                .ToList());
    }

    internal static WorkflowGraph BuildGraph(WorkflowDescriptor workflow) => new(
        workflow.Graph.Start,
        workflow.Graph.Nodes.Select(n => new GraphNode(n.Id, n.Name, n.Kind, n.Outcome)).ToList(),
        workflow.Graph.Edges.Select(e => new GraphEdge(e.From, e.To, e.Label)).ToList());

    private static IResult ToResult((HumanInputResult Result, string? Error) outcome) => outcome.Result switch
    {
        HumanInputResult.Completed => Results.NoContent(),
        HumanInputResult.NotFound => NotFound(outcome.Error!),
        _ => Conflict(outcome.Error!),
    };

    private static IResult NotFound(string message) =>
        Results.Problem(title: "Not found", detail: message, statusCode: StatusCodes.Status404NotFound);

    private static IResult Conflict(string message) =>
        Results.Problem(title: "Conflict", detail: message, statusCode: StatusCodes.Status409Conflict);
}
