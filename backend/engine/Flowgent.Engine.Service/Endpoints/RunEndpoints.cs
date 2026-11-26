namespace Flowgent.Engine.Service.Endpoints;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Core.Runs.Execution;
using Flowgent.Engine.Core.Runs.Intake;
using Flowgent.Engine.Core.Runs.Store;
using Flowgent.Engine.Service.Endpoints.Primitives;
using Microsoft.AspNetCore.Mvc;

/// <summary>Submitting, listing, inspecting and aborting workflow runs.</summary>
internal sealed class RunEndpoints : IEndpoint
{
    private const int DefaultRunsLimit = 500;
    private const int MaxRunsLimit = 1000;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EngineRoutes.Base).WithTags("Runs");

        group.MapPost("/workflows/{workflowId}/runs", SubmitRun)
            .WithSummary("Submit an input to a workflow (async: saved to the inbox, returns 202)");

        group.MapGet("/runs", ListRuns)
            .WithSummary("Runs, newest first, filterable by module, workflow and status");

        group.MapGet("/runs/{runId:guid}", GetRun)
            .WithSummary("A run with input, step outputs and final output");

        group.MapPost("/runs/{runId:guid}/abort", AbortRun)
            .WithSummary("Abort a run; it ends in Failed state with reason 'Aborted by user'");
    }

    private static async Task<IResult> SubmitRun(string workflowId, [FromBody] JsonNode? input, IntakeService intake, CancellationToken ct) =>
        ToResult(await intake.SubmitAsync(workflowId, input, ct));

    private static async Task<IResult> ListRuns(IRunStore store, string? moduleId, string? workflowId, RunStatus? status, int? limit, CancellationToken ct)
    {
        var query = new RunQuery(moduleId, workflowId, status, Math.Clamp(limit ?? DefaultRunsLimit, 1, MaxRunsLimit));
        return Results.Ok((await store.ListAsync(query, ct)).Select(r => r.ToSummary()));
    }

    private static async Task<IResult> GetRun(Guid runId, IRunStore store, CancellationToken ct) =>
        await store.GetAsync(runId, ct) is { } run ? Results.Ok(run.ToDetail()) : ApiResults.NotFound($"Run {runId} not found.");

    private static async Task<IResult> AbortRun(Guid runId, IRunStore store, RunRegistry registry, CancellationToken ct)
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
            return ApiResults.NotFound($"Run {runId} not found.");
        }

        if (run.Status.IsTerminal())
        {
            return ApiResults.Conflict($"Run {runId} has already completed ({run.Status}).");
        }

        // Queued (or orphaned) run: end it here; the inbox processor skips terminal runs.
        await store.RecordAsync(new RunTransition(runId, RunEventKind.Failed, RunStatus.Failed, run.CurrentStepId, RunRegistry.AbortReason), ct);
        return Results.NoContent();
    }

    private static IResult ToResult(IntakeResult result) => result switch
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
        IntakeResult.NotFound notFound => ApiResults.NotFound(notFound.Message),
        IntakeResult.Unavailable unavailable => Results.Problem(
            title: "Module unavailable", detail: unavailable.Message, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };
}
