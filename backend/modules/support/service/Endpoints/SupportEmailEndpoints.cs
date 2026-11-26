namespace Flowgent.Modules.Support.Endpoints;

using Flowgent.Contracts.Api;
using Flowgent.Module.Sdk;

/// <summary>A support email, as accepted by <c>POST /api/support/emails</c> (property names are case-insensitive).</summary>
public sealed record IncomingEmail(string? Sender, string? Subject, string? Body);

public static class SupportEmailEndpoints
{
    public const string RequiredMessage = "Subject and Body are required.";

    /// <summary>
    /// <c>POST /api/support/emails</c> (kept for compatibility, routed by the gateway) and
    /// <c>POST /api/modules/support/emails</c>. Both forward to the engine's generic intake.
    /// </summary>
    public static IEndpointRouteBuilder MapSupportEmailEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/support/emails", SubmitAsync).WithTags("Support emails")
            .WithSummary("Submit a support email (async: saved to the engine inbox, returns 202)");
        app.MapPost($"/api/modules/{SupportHost.ModuleId}/emails", SubmitAsync).WithTags("Support emails")
            .WithSummary("Submit a support email (module route)");
        return app;
    }

    private static async Task<IResult> SubmitAsync(IncomingEmail email, IEngineClient engine, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email.Subject) || string.IsNullOrWhiteSpace(email.Body))
        {
            return Results.BadRequest(new { error = RequiredMessage });
        }

        var input = new { sender = email.Sender ?? "", subject = email.Subject, body = email.Body };
        return await engine.SubmitAsync(SupportHost.WorkflowId, input, ct) switch
        {
            EngineSubmitResult.Accepted accepted => Results.Accepted(
                EngineRoutes.Run(accepted.Ids.RunId),
                new { messageId = accepted.Ids.MessageId, runId = accepted.Ids.RunId }),
            EngineSubmitResult.Invalid invalid => Results.BadRequest(new { error = invalid.Message }),
            EngineSubmitResult.NotFound notFound => Results.Json(new { error = notFound.Message }, statusCode: StatusCodes.Status503ServiceUnavailable),
            EngineSubmitResult.Unavailable unavailable => Results.Json(new { error = unavailable.Message }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
