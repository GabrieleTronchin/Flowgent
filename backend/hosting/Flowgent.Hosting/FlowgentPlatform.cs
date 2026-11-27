namespace Flowgent.Hosting;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

/// <summary>
/// Wires the Flowgent platform into an Aspire AppHost: engine, gateway and dashboard, then module services and MCP
/// servers. Shared by the core AppHost and the samples AppHost, so both start the platform the same way.
/// </summary>
/// <example>
/// <code>
/// var builder = DistributedApplication.CreateBuilder(args);
/// var platform = FlowgentPlatform.Add&lt;Projects.Flowgent_Engine_Service, Projects.Flowgent_Gateway&gt;(builder, "../../dashboard");
/// platform.AddModule&lt;Projects.Flowgent_Modules_Template&gt;("template");
/// builder.Build().Run();
/// </code>
/// </example>
public sealed class FlowgentPlatform
{
    /// <summary>Port of the dashboard (Vite dev server).</summary>
    public const int DashboardPort = 5173;

    private readonly IDistributedApplicationBuilder _builder;
    private int _moduleIndex;
    private int _mcpServerIndex;

    private FlowgentPlatform(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> engine, IResourceBuilder<ProjectResource> gateway)
    {
        _builder = builder;
        Engine = engine;
        Gateway = gateway;
    }

    public IResourceBuilder<ProjectResource> Engine { get; }

    public IResourceBuilder<ProjectResource> Gateway { get; }

    /// <summary>
    /// Adds the engine, the gateway and the dashboard. <c>AppHost:LlmProvider</c> (e.g. <c>Demo</c>) overrides the
    /// engine's LLM provider. <paramref name="dashboardPath"/> is the dashboard folder, relative to the AppHost.
    /// </summary>
    public static FlowgentPlatform Add<TEngine, TGateway>(IDistributedApplicationBuilder builder, string dashboardPath)
        where TEngine : IProjectMetadata, new()
        where TGateway : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        var engine = builder.AddProject<TEngine>("engine");
        if (builder.Configuration["AppHost:LlmProvider"] is { Length: > 0 } llmProvider)
        {
            engine.WithEnvironment("Flowgent__Llm__Provider", llmProvider);
        }

        var gateway = builder.AddProject<TGateway>("gateway")
            .WithReference(engine)
            .WithEnvironment("Gateway__Engine__Address", "http://engine");

        builder.AddViteApp("dashboard", dashboardPath)
            .WithReference(gateway)
            .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"))
            .WithEndpoint("http", endpoint => endpoint.Port = DashboardPort)
            .WaitFor(gateway);

        return new FlowgentPlatform(builder, engine, gateway);
    }

    /// <summary>
    /// Adds a module service and wires it in both directions: the module calls the engine intake, the engine pulls the
    /// module package and calls its tools, the gateway routes <c>/api/modules/{id}/**</c> (and extra routes) to it.
    /// <c>AppHost:Modules:{id}:Enabled=false</c> leaves the module out.
    /// </summary>
    public FlowgentPlatform AddModule<TProject>(string id, params string[] gatewayRoutes)
        where TProject : IProjectMetadata, new()
    {
        if (!IsEnabled(id))
        {
            return this;
        }

        var module = _builder.AddProject<TProject>(id)
            .WithReference(Engine)
            .WithEnvironment("Flowgent__Module__EngineUrl", "http://engine");

        // No WaitFor: the engine starts first and retries modules that are not up yet.
        Engine.WithReference(module)
            .WithEnvironment($"Flowgent__Modules__{_moduleIndex}__Id", id)
            .WithEnvironment($"Flowgent__Modules__{_moduleIndex}__Url", $"http://{id}");

        Gateway.WithReference(module)
            .WithEnvironment($"Gateway__Modules__{_moduleIndex}__Id", id)
            .WithEnvironment($"Gateway__Modules__{_moduleIndex}__Address", $"http://{id}");
        for (var route = 0; route < gatewayRoutes.Length; route++)
        {
            Gateway.WithEnvironment($"Gateway__Modules__{_moduleIndex}__ExtraRoutes__{route}", gatewayRoutes[route]);
        }

        _moduleIndex++;
        return this;
    }

    /// <summary>
    /// Adds an MCP server (Streamable HTTP) and maps the workflow <c>serverLabel</c> <paramref name="label"/> to it in the
    /// engine (<c>Flowgent:McpServers[]</c>). The resource is named <c>&lt;label&gt;-mcp</c>.
    /// </summary>
    public IResourceBuilder<ProjectResource> AddMcpServer<TProject>(string label)
        where TProject : IProjectMetadata, new()
    {
        var name = $"{label}-mcp";
        var server = _builder.AddProject<TProject>(name);

        Engine.WithReference(server)
            .WithEnvironment($"Flowgent__McpServers__{_mcpServerIndex}__Label", label)
            .WithEnvironment($"Flowgent__McpServers__{_mcpServerIndex}__Url", $"http://{name}");

        _mcpServerIndex++;
        return server;
    }

    private bool IsEnabled(string id) =>
        !bool.TryParse(_builder.Configuration[$"AppHost:Modules:{id}:Enabled"], out var enabled) || enabled;
}
