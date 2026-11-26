namespace Flowgent.Engine.Service.Endpoints;

using Flowgent.Contracts.Api;
using Flowgent.Engine.Core.Runs.HumanInput;
using Flowgent.Engine.Service.Endpoints.Primitives;

/// <summary>Runs awaiting a human decision, and the approve / reject actions.</summary>
internal sealed class HumanInputEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{EngineRoutes.Base}/human-inputs").WithTags("Human input");

        group.MapGet("", ListPending)
            .WithSummary("Runs awaiting human input, with their review payload");

        group.MapPost("/{runId:guid}/approve", Approve)
            .WithSummary("Approve a run awaiting input");

        group.MapPost("/{runId:guid}/reject", Reject)
            .WithSummary("Reject a run awaiting input, with an optional reason");
    }

    private static IResult ListPending(HumanInputRegistry registry, string? moduleId, string? workflowId) =>
        Results.Ok(registry.GetPending(moduleId, workflowId).Select(p => p.ToInfo()));

    private static async Task<IResult> Approve(Guid runId, HumanInputService service, CancellationToken ct) =>
        ToResult(await service.ApproveAsync(runId, ct));

    private static async Task<IResult> Reject(Guid runId, RejectRequest? request, HumanInputService service, CancellationToken ct) =>
        ToResult(await service.RejectAsync(runId, request?.Reason, ct));

    private static IResult ToResult((HumanInputResult Result, string? Error) outcome) => outcome.Result switch
    {
        HumanInputResult.Completed => Results.NoContent(),
        HumanInputResult.NotFound => ApiResults.NotFound(outcome.Error!),
        _ => ApiResults.Conflict(outcome.Error!),
    };
}
