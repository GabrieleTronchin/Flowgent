namespace Flowgent.Engine.Service;

using System.Text.Json.Serialization;
using Flowgent.Engine.Core;
using Flowgent.Engine.Service.Endpoints.Primitives;
using Flowgent.Persistence;

/// <summary>
/// Builds the engine application. <c>Program.cs</c> and the in-process test harness both use it.
/// <paramref name="configure"/> runs before the engine services are registered, so it can add configuration
/// and replace abstractions registered with TryAdd (e.g. the HTTP primary handler in tests).
/// </summary>
public static class EngineHost
{
    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddProblemDetails();
        builder.Services.AddGrpc();
        builder.Services.AddEndpoints(typeof(EngineHost).Assembly);

        builder.Services.AddFlowgentRuntime(builder.Configuration);
        builder.Services.AddFlowgentPersistence();

        var app = builder.Build();

        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
        app.MapGrpcService<RunMonitorService>();
        app.MapEndpoints();
        app.MapDefaultEndpoints();

        return app;
    }
}
