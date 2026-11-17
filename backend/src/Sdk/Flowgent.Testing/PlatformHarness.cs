namespace Flowgent.Testing;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Service;
using Flowgent.Gateway;
using Flowgent.Runtime.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

/// <summary>Creates a service application (the <c>XxxHost.Create</c> methods of Flowgent services).</summary>
public delegate WebApplication ServiceFactory(WebApplicationOptions options, Action<WebApplicationBuilder>? configure);

/// <summary>A module service started by the harness.</summary>
public sealed record ModuleRegistration(string Id, ServiceFactory Create, string ContentRoot, IReadOnlyList<string> GatewayRoutes);

/// <summary>An MCP server started by the harness; the engine reaches it over in-process HTTP by <see cref="Id"/>.</summary>
public sealed record McpServerRegistration(string Id, ServiceFactory Create, string ContentRoot);

public sealed class PlatformOptions
{
    internal List<ModuleRegistration> Modules { get; } = [];

    internal List<McpServerRegistration> McpServers { get; } = [];

    internal Dictionary<string, string?> EngineSettings { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, Dictionary<string, string?>> ModuleSettings { get; } = new(StringComparer.Ordinal);

    internal bool UseGateway { get; private set; }

    /// <summary>Adds a module service; <paramref name="contentRoot"/> is relative to the repository root.</summary>
    public PlatformOptions AddModule(string id, ServiceFactory create, string contentRoot, params string[] gatewayRoutes)
    {
        Modules.Add(new ModuleRegistration(id, create, Path.Combine(PlatformHarness.RepositoryRoot, contentRoot), gatewayRoutes));
        return this;
    }

    /// <summary>
    /// Adds a standalone MCP server; the engine reaches it over in-process HTTP at <c>http://{id}</c> (mapped to the
    /// server's TestServer by the router) and resolves it from <c>Flowgent:McpServers[]</c> by the matching label.
    /// <paramref name="contentRoot"/> is relative to the repository root.
    /// </summary>
    public PlatformOptions AddMcpServer(string id, ServiceFactory create, string contentRoot)
    {
        McpServers.Add(new McpServerRegistration(id, create, Path.Combine(PlatformHarness.RepositoryRoot, contentRoot)));
        return this;
    }

    public PlatformOptions WithGateway()
    {
        UseGateway = true;
        return this;
    }

    /// <summary>Engine configuration value (e.g. <c>Flowgent:Llm:DemoLatencyMs</c>).</summary>
    public PlatformOptions ConfigureEngine(string key, string? value)
    {
        EngineSettings[key] = value;
        return this;
    }

    public PlatformOptions ConfigureModule(string moduleId, string key, string? value)
    {
        if (!ModuleSettings.TryGetValue(moduleId, out var settings))
        {
            ModuleSettings[moduleId] = settings = new(StringComparer.Ordinal);
        }

        settings[key] = value;
        return this;
    }
}

/// <summary>
/// Runs the engine, module services and (optionally) the gateway in-process on TestServers. Services reach each
/// other by host name (<c>http://engine</c>, <c>http://{moduleId}</c>) through a <see cref="HostRoutingHandler"/>,
/// so a module can be stopped to test unavailability. Demo mode without latency by default.
/// </summary>
public sealed class PlatformHarness : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly Lazy<string> Root = new(FindRepositoryRoot);
    private readonly PlatformOptions _options;
    private readonly Dictionary<string, WebApplication> _modules;
    private readonly Dictionary<string, WebApplication> _mcpServers;

    private PlatformHarness(PlatformOptions options, HostRoutingHandler router, WebApplication engine, Dictionary<string, WebApplication> modules, Dictionary<string, WebApplication> mcpServers, WebApplication? gateway)
    {
        _options = options;
        Router = router;
        Engine = engine;
        _modules = modules;
        _mcpServers = mcpServers;
        Gateway = gateway;
        EngineClient = engine.GetTestClient();
        GatewayClient = gateway?.GetTestClient();
    }

    /// <summary>Repository root (folder containing <c>backend/Flowgent.slnx</c>).</summary>
    public static string RepositoryRoot => Root.Value;

    public HostRoutingHandler Router { get; }

    public WebApplication Engine { get; }

    public WebApplication? Gateway { get; }

    public HttpClient EngineClient { get; }

    public HttpClient? GatewayClient { get; }

    /// <summary>The gateway client when the gateway is enabled, otherwise the engine client.</summary>
    public HttpClient Client => GatewayClient ?? EngineClient;

    public WebApplication Module(string id) => _modules[id];

    public HttpClient ModuleClient(string id) => _modules[id].GetTestClient();

    public static async Task<PlatformHarness> StartAsync(Action<PlatformOptions> configure)
    {
        var options = new PlatformOptions();
        configure(options);
        var router = new HostRoutingHandler();

        var modules = new Dictionary<string, WebApplication>(StringComparer.Ordinal);
        foreach (var module in options.Modules)
        {
            modules[module.Id] = await StartModuleServiceAsync(module, options.ModuleSettings.GetValueOrDefault(module.Id), router);
        }

        var mcpServers = new Dictionary<string, WebApplication>(StringComparer.Ordinal);
        foreach (var server in options.McpServers)
        {
            mcpServers[server.Id] = await StartMcpServerAsync(server, options.ModuleSettings.GetValueOrDefault(server.Id), router);
        }

        var engineSettings = new Dictionary<string, string?>
        {
            ["Flowgent:InboxPollingIntervalSeconds"] = "0.05",
            ["Flowgent:Llm:Provider"] = "Demo",
            ["Flowgent:Llm:DemoLatencyMs"] = "0",
        };
        for (var i = 0; i < options.Modules.Count; i++)
        {
            engineSettings[$"Flowgent:Modules:{i}:Id"] = options.Modules[i].Id;
            engineSettings[$"Flowgent:Modules:{i}:Url"] = $"http://{options.Modules[i].Id}";
            engineSettings[$"Flowgent:Modules:{i}:Retries"] = "0";
            engineSettings[$"Flowgent:Modules:{i}:RefreshSeconds"] = "0.2";
        }

        for (var i = 0; i < options.McpServers.Count; i++)
        {
            // Label is the id (the workflow's serverLabel); the router host is "{id}-mcp" so an MCP server and a module
            // service can share the same id without colliding on one router host (the engine fetches the module package
            // from http://{id} and the MCP tool from http://{id}-mcp).
            engineSettings[$"Flowgent:McpServers:{i}:Label"] = options.McpServers[i].Id;
            engineSettings[$"Flowgent:McpServers:{i}:Url"] = McpHost(options.McpServers[i].Id);
        }

        foreach (var (key, value) in options.EngineSettings)
        {
            engineSettings[key] = value;
        }

        var engine = EngineHost.Create(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = RepositoryRoot }, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(engineSettings);
            builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(router.CreateClientHandler));
            // Override the production HttpMcpTransportFactory (registered with TryAdd) so the engine reaches the
            // harness's in-process MCP TestServers over the router instead of a real socket (FEAT-003).
            builder.Services.AddSingleton<Flowgent.Runtime.Maf.IMcpTransportFactory>(sp =>
                new RoutedMcpTransportFactory(router, sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        });
        await engine.StartAsync();
        router.Register("engine", engine.GetTestServer().CreateHandler());

        WebApplication? gateway = null;
        if (options.UseGateway)
        {
            var gatewaySettings = new Dictionary<string, string?> { ["Gateway:Engine:Address"] = "http://engine" };
            for (var i = 0; i < options.Modules.Count; i++)
            {
                gatewaySettings[$"Gateway:Modules:{i}:Id"] = options.Modules[i].Id;
                gatewaySettings[$"Gateway:Modules:{i}:Address"] = $"http://{options.Modules[i].Id}";
                for (var r = 0; r < options.Modules[i].GatewayRoutes.Count; r++)
                {
                    gatewaySettings[$"Gateway:Modules:{i}:ExtraRoutes:{r}"] = options.Modules[i].GatewayRoutes[r];
                }
            }

            gateway = GatewayHost.Create(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = RepositoryRoot }, builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(gatewaySettings);
                builder.Services.AddSingleton<IForwarderHttpClientFactory>(new TestForwarderHttpClientFactory(router));
            });
            await gateway.StartAsync();
        }

        var harness = new PlatformHarness(options, router, engine, modules, mcpServers, gateway);
        await harness.RefreshModulesAsync();
        return harness;
    }

    /// <summary>Makes the engine check every module now.</summary>
    public Task RefreshModulesAsync() => Engine.Services.GetRequiredService<ModuleCatalogService>().RefreshAllAsync();

    /// <summary>Simulates a stopped module service (connections refused) and lets the engine notice it.</summary>
    public async Task StopModuleAsync(string id)
    {
        Router.SetDown(id);
        await RefreshModulesAsync();
    }

    public async Task StartModuleAsync(string id)
    {
        Router.SetDown(id, down: false);
        await RefreshModulesAsync();
    }

    /// <summary>
    /// Stops the module service and starts a new instance (same factory and settings, e.g. after its definition
    /// files changed), then lets the engine reload it. Simulates a module redeployed with a new version.
    /// </summary>
    public async Task RestartModuleAsync(string id)
    {
        var registration = _options.Modules.Single(m => m.Id == id);
        var previous = _modules[id];
        Router.SetDown(id);
        await previous.StopAsync();
        await previous.DisposeAsync();

        _modules[id] = await StartModuleServiceAsync(registration, _options.ModuleSettings.GetValueOrDefault(id), Router);
        Router.SetDown(id, down: false);
        await RefreshModulesAsync();
    }

    public async Task<IntakeAccepted> SubmitAsync(string workflowId, object input)
    {
        var response = await EngineClient.PostAsJsonAsync(EngineRoutes.SubmitRun(workflowId), input, Json);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Intake returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<IntakeAccepted>(Json))!;
    }

    public async Task<RunDetail> GetRunAsync(Guid runId) =>
        (await EngineClient.GetFromJsonAsync<RunDetail>(EngineRoutes.Run(runId), Json))!;

    // Waits are generous: a MAF declarative run carries per-run JSON checkpoint I/O, so under the load of the whole
    // test solution running in parallel a run can take noticeably longer than on a quiet machine to reach HITL/terminal.
    public async Task<RunDetail> WaitForRunAsync(Guid runId, Func<RunDetail, bool> condition, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var run = await GetRunAsync(runId);
            if (condition(run))
            {
                return run;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Run {runId} did not reach the expected state. Last: {run.Status} at {run.CurrentStepId} ({run.Detail})");
            }

            await Task.Delay(20);
        }
    }

    public Task<RunDetail> WaitForTerminalAsync(Guid runId) => WaitForRunAsync(runId, r => r.Status.IsTerminal());

    /// <summary>Waits until the run's human input can be approved or rejected.</summary>
    public async Task<HumanInputInfo> WaitForHumanInputAsync(Guid runId, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var pending = await EngineClient.GetFromJsonAsync<List<HumanInputInfo>>($"{EngineRoutes.Base}/human-inputs", Json);
            if (pending!.FirstOrDefault(p => p.RunId == runId) is { } input)
            {
                return input;
            }

            if (DateTime.UtcNow > deadline)
            {
                var run = await GetRunAsync(runId);
                throw new TimeoutException($"Run {runId} is not awaiting input. Last: {run.Status} at {run.CurrentStepId} ({run.Detail})");
            }

            await Task.Delay(20);
        }
    }

    public Task<HttpResponseMessage> ApproveAsync(Guid runId) =>
        EngineClient.PostAsync($"{EngineRoutes.Base}/human-inputs/{runId}/approve", null);

    public Task<HttpResponseMessage> RejectAsync(Guid runId, string? reason) =>
        EngineClient.PostAsJsonAsync($"{EngineRoutes.Base}/human-inputs/{runId}/reject", new RejectRequest(reason), Json);

    public Task<HttpResponseMessage> AbortAsync(Guid runId) =>
        EngineClient.PostAsync($"{EngineRoutes.Base}/runs/{runId}/abort", null);

    public async Task<IReadOnlyList<RunEventInfo>> GetEventsAsync(Guid runId) =>
        ((await EngineClient.GetFromJsonAsync<List<RunEventInfo>>($"{EngineRoutes.Base}/events?runId={runId}", Json))!).AsEnumerable().Reverse().ToList();

    public async ValueTask DisposeAsync()
    {
        if (Gateway is not null)
        {
            await Gateway.StopAsync();
            await Gateway.DisposeAsync();
        }

        await Engine.StopAsync();
        await Engine.DisposeAsync();
        foreach (var module in _modules.Values)
        {
            await module.StopAsync();
            await module.DisposeAsync();
        }

        foreach (var server in _mcpServers.Values)
        {
            await server.StopAsync();
            await server.DisposeAsync();
        }
    }

    private static async Task<WebApplication> StartModuleServiceAsync(
        ModuleRegistration module,
        IReadOnlyDictionary<string, string?>? moduleSettings,
        HostRoutingHandler router)
    {
        var settings = new Dictionary<string, string?> { ["Flowgent:Module:EngineUrl"] = "http://engine" };
        foreach (var (key, value) in moduleSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var app = module.Create(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = module.ContentRoot }, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(router.CreateClientHandler));
        });
        await app.StartAsync();
        router.Register(module.Id, app.GetTestServer().CreateHandler());
        return app;
    }

    private static async Task<WebApplication> StartMcpServerAsync(
        McpServerRegistration server,
        IReadOnlyDictionary<string, string?>? serverSettings,
        HostRoutingHandler router)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var (key, value) in serverSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var app = server.Create(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = server.ContentRoot }, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(router.CreateClientHandler));
        });
        await app.StartAsync();
        router.Register(new Uri(McpHost(server.Id)).Host, app.GetTestServer().CreateHandler());
        return app;
    }

    /// <summary>The in-process MCP URL (and router host) for a server id: distinct from the module host of the same id.</summary>
    private static string McpHost(string id) => $"http://{id}-mcp";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "Flowgent.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (folder containing backend/Flowgent.slnx) not found.");
    }

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
