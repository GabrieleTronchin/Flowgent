namespace Flowgent.Mcp.Support;

using Flowgent.Mcp.Support.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builds the standalone support MCP server (Model B). It exposes the single <c>locateComponent</c> tool over the MCP
/// Streamable-HTTP transport and nothing else — no module SDK, contracts, definitions or engine code. <c>Program.cs</c>
/// and the in-process test harness both use this factory (hosts-as-factories pattern); <paramref name="configure"/>
/// runs before the services are registered (configuration, test transport).
/// </summary>
public static class SupportMcpHost
{
    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();

        builder.Services.Configure<SupportMcpOptions>(o =>
        {
            if (builder.Configuration[SupportMcpOptions.EnvironmentVariable] is { Length: > 0 } root)
            {
                o.DummyAppsRoot = root;
            }
        });

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<LocateComponentMcpTool>();

        var app = builder.Build();

        app.MapMcp();
        app.MapDefaultEndpoints();

        return app;
    }
}
