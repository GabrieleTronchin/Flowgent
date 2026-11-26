namespace Flowgent.Engine.Service.Endpoints;

using Flowgent.Contracts.Api;
using Flowgent.Engine.Core.Runs.Intake;
using Flowgent.Engine.Service.Endpoints.Primitives;

/// <summary>Inbox messages (queued, processed, failed).</summary>
internal sealed class InboxEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{EngineRoutes.Base}/inbox").WithTags("Monitoring");

        group.MapGet("", ListMessages)
            .WithSummary("Inbox messages (queued, processed, failed)");

        group.MapGet("/{messageId:guid}", GetMessage)
            .WithSummary("An inbox message");
    }

    private static async Task<IResult> ListMessages(IInboxStore inbox, string? status, string? moduleId, CancellationToken ct) =>
        Results.Ok(await inbox.ListAsync(status, moduleId, ct));

    private static async Task<IResult> GetMessage(Guid messageId, IInboxStore inbox, CancellationToken ct) =>
        (await inbox.ListAsync(null, null, ct)).FirstOrDefault(m => m.Id == messageId) is { } message
            ? Results.Ok(message)
            : ApiResults.NotFound($"Inbox message {messageId} not found.");
}
