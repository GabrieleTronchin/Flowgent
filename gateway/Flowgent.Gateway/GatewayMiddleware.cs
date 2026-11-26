namespace Flowgent.Gateway;

using Microsoft.AspNetCore.Http.HttpResults;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

public static class GatewayMiddleware
{
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>
    /// Reuses the caller's correlation id or creates one, forwards it downstream (YARP copies request headers)
    /// and returns it to the caller.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
            {
                correlationId = Guid.NewGuid().ToString("N");
                context.Request.Headers[CorrelationIdHeader] = correlationId;
            }

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationIdHeader] = correlationId;
                return Task.CompletedTask;
            });

            await next(context);
        });

    /// <summary>
    /// Proxy-pipeline step: when the destination cannot be reached (and nothing has been sent yet),
    /// replaces YARP's empty 502/504 with a 503 problem-details body naming the service.
    /// </summary>
    public static async Task WriteServiceUnavailableOnForwarderError(HttpContext context, Func<Task> next)
    {
        await next();

        var error = context.Features.Get<IForwarderErrorFeature>();
        if (error is null || context.Response.HasStarted || error.Error is ForwarderError.RequestCanceled)
        {
            return;
        }

        var service = context.Features.Get<IReverseProxyFeature>()?.Cluster?.Config.Metadata?
            .GetValueOrDefault(GatewayRoutes.ServiceMetadataKey) ?? "unknown";

        var problem = TypedResults.Problem(
            title: "Service unavailable",
            detail: $"The '{service}' service is unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["service"] = service });

        context.Response.Clear();
        await ((IResult)problem).ExecuteAsync(context);
    }
}
