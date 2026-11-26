// Starts the whole platform: engine, module services, gateway (http://localhost:5080) and dashboard (http://localhost:5173).
// Run: dotnet run --project Flowgent.AppHost
var builder = DistributedApplication.CreateBuilder(args);

var engine = builder.AddProject<Projects.Flowgent_Engine_Service>("engine");

// Optional override of the engine's LLM provider, e.g. `dotnet run --project ... -- --AppHost:LlmProvider=Demo`.
if (builder.Configuration["AppHost:LlmProvider"] is { Length: > 0 } llmProvider)
{
    engine.WithEnvironment("Flowgent__Llm__Provider", llmProvider);
}

var gateway = builder.AddProject<Projects.Flowgent_Gateway>("gateway")
    .WithReference(engine)
    .WithEnvironment("Gateway__Engine__Address", "http://engine");

var modules = new ModuleWiring(builder, engine, gateway);

// The support module is the reference module: keep it first (index 0 matches the standalone appsettings).
modules.Add<Projects.Flowgent_Modules_Support>("support", "/api/support/{**catch-all}");

// Example module (modules/_template), disabled by default: --AppHost:Modules:template:Enabled=true turns it on.
modules.Add<Projects.Flowgent_Modules_Template>("template");

// Jokes: a second simple example module (modules/jokes), enabled by default.
modules.Add<Projects.Flowgent_Modules_Jokes>("jokes");

builder.AddViteApp("dashboard", "../dashboard")
    .WithReference(gateway)
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WaitFor(gateway);

builder.Build().Run();

/// <summary>
/// Registers a module service and wires it in both directions: the module calls the engine intake, the engine pulls
/// the module package and calls its tools, the gateway routes <c>/api/modules/{id}/**</c> (and extra routes) to it.
/// A module can be disabled with <c>AppHost:Modules:{id}:Enabled=false</c>.
/// </summary>
internal sealed class ModuleWiring(
    IDistributedApplicationBuilder builder,
    IResourceBuilder<ProjectResource> engine,
    IResourceBuilder<ProjectResource> gateway)
{
    private int _index;

    public void Add<TProject>(string id, params string[] gatewayRoutes)
        where TProject : IProjectMetadata, new()
    {
        if (bool.TryParse(builder.Configuration[$"AppHost:Modules:{id}:Enabled"], out var enabled) && !enabled)
        {
            return;
        }

        var module = builder.AddProject<TProject>(id)
            .WithReference(engine)
            .WithEnvironment("Flowgent__Module__EngineUrl", "http://engine");

        // No WaitFor: the engine starts first and retries modules that are not up yet.
        engine.WithReference(module)
            .WithEnvironment($"Flowgent__Modules__{_index}__Id", id)
            .WithEnvironment($"Flowgent__Modules__{_index}__Url", $"http://{id}");

        gateway.WithReference(module)
            .WithEnvironment($"Gateway__Modules__{_index}__Id", id)
            .WithEnvironment($"Gateway__Modules__{_index}__Address", $"http://{id}");
        for (var route = 0; route < gatewayRoutes.Length; route++)
        {
            gateway.WithEnvironment($"Gateway__Modules__{_index}__ExtraRoutes__{route}", gatewayRoutes[route]);
        }

        _index++;
    }
}
