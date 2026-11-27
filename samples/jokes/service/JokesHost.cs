namespace Flowgent.Modules.Jokes;

using Flowgent.Module.Sdk;

/// <summary>
/// Builds the jokes module service. <c>Program.cs</c> uses it.
/// <paramref name="configure"/> runs before the services are registered (configuration, test handlers).
/// </summary>
public static class JokesHost
{
    public const string ModuleId = "jokes";
    public const string WorkflowId = "joke";

    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();
        builder.AddFlowgentModule();

        var app = builder.Build();

        app.MapFlowgentModule();
        app.MapDefaultEndpoints();

        return app;
    }
}
