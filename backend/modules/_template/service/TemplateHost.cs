namespace Flowgent.Modules.Template;

using Flowgent.Module.Sdk;
using Flowgent.Modules.Template.Tools;

/// <summary>
/// Builds the template module service. <c>Program.cs</c> and the in-process test harness both use it.
/// <paramref name="configure"/> runs before the services are registered (configuration, test handlers).
/// </summary>
public static class TemplateHost
{
    public const string ModuleId = "template";
    public const string WorkflowId = "template-hello";

    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();
        builder.AddFlowgentModule();
        builder.Services.AddModuleTool<WordCountTool>(WordCountTool.Name);

        var app = builder.Build();

        app.MapFlowgentModule();
        app.MapDefaultEndpoints();

        return app;
    }
}
