namespace Flowgent.Modules.Support;

using Flowgent.Module.Sdk;
using Flowgent.Modules.Support.Endpoints;

/// <summary>
/// Builds the support module service. <c>Program.cs</c> and the in-process test harness both use it.
/// <paramref name="configure"/> runs before the services are registered (configuration, test handlers).
/// </summary>
public static class SupportHost
{
    public const string ModuleId = "support";
    public const string WorkflowId = "support-email";

    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();
        builder.AddFlowgentModule();

        var app = builder.Build();

        app.MapFlowgentModule();
        app.MapSupportEmailEndpoints();
        app.MapDefaultEndpoints();

        return app;
    }
}
