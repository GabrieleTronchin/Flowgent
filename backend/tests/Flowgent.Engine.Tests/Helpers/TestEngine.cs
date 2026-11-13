namespace Flowgent.Engine.Tests.Helpers;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Contracts.Runs;
using Flowgent.Persistence;
using Flowgent.Runtime;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.HumanInput;
using Flowgent.Runtime.Intake;
using Flowgent.Runtime.Llm;
using Flowgent.Runtime.Modules;
using Flowgent.Runtime.Runs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The engine runtime and persistence wired through DI, with an in-memory module client (no HTTP).
/// Demo mode without latency by default.
/// </summary>
public sealed class TestEngine : IAsyncDisposable
{
    private TestEngine(ServiceProvider services, FakeModuleClient modules)
    {
        Services = services;
        Modules = modules;
    }

    public ServiceProvider Services { get; }

    public FakeModuleClient Modules { get; }

    public IRunStore Store => Services.GetRequiredService<IRunStore>();

    public IntakeService Intake => Services.GetRequiredService<IntakeService>();

    public RunOrchestrator Orchestrator => Services.GetRequiredService<RunOrchestrator>();

    public HumanInputService HumanInput => Services.GetRequiredService<HumanInputService>();

    public HumanInputRegistry HumanInputs => Services.GetRequiredService<HumanInputRegistry>();

    public RunRegistry Registry => Services.GetRequiredService<RunRegistry>();

    public LlmTelemetryStore Telemetry => Services.GetRequiredService<LlmTelemetryStore>();

    public InboxProcessor Inbox => Services.GetRequiredService<InboxProcessor>();

    public static async Task<TestEngine> StartAsync(
        Action<Dictionary<string, string>>? patch = null,
        Action<IServiceCollection>? services = null,
        IDictionary<string, string?>? configuration = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Flowgent:Modules:0:Id"] = "demo",
            ["Flowgent:Modules:0:Url"] = "http://demo",
            ["Flowgent:Modules:0:Retries"] = "0",
            ["Flowgent:InboxPollingIntervalSeconds"] = "0.05",
            ["Flowgent:Llm:Provider"] = "Demo",
            ["Flowgent:Llm:DemoLatencyMs"] = "0",
        };
        foreach (var (key, value) in configuration ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var modules = new FakeModuleClient();
        modules.SetPackage("demo", TestModules.Package(patch));
        modules.Tools["demo.count"] = input => new JsonObject { ["words"] = input?["text"]?.GetValue<string>().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0 };

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<IConfiguration>(config);
        collection.AddSingleton<IModuleClient>(modules);
        services?.Invoke(collection);
        collection.AddFlowgentRuntime(config);
        collection.AddFlowgentPersistence();

        var provider = collection.BuildServiceProvider();
        await provider.GetRequiredService<ModuleCatalogService>().RefreshAllAsync();
        return new TestEngine(provider, modules);
    }

    /// <summary>Submits an input and returns the queued run id (fails the test when rejected).</summary>
    public async Task<Guid> SubmitAsync(JsonObject input, string workflowId = "hello")
    {
        var result = await Intake.SubmitAsync(workflowId, input);
        return result is IntakeResult.Accepted accepted
            ? accepted.RunId
            : throw new InvalidOperationException($"Intake rejected: {result}");
    }

    /// <summary>Submits and executes a run to its end (no human input expected).</summary>
    public async Task<RunRecord> RunAsync(JsonObject input, string workflowId = "hello")
    {
        var runId = await SubmitAsync(input, workflowId);
        await Orchestrator.ExecuteAsync(runId);
        return (await Store.GetAsync(runId))!;
    }

    public async Task<RunRecord> WaitForAsync(Guid runId, Func<RunRecord, bool> condition, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var run = await Store.GetAsync(runId);
            if (run is not null && condition(run))
            {
                return run;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Run {runId} did not reach the expected state. Last: {run?.Status} at {run?.CurrentStepId} ({run?.Detail})");
            }

            await Task.Delay(10);
        }
    }

    public Task<RunRecord> WaitForStatusAsync(Guid runId, RunStatus status, int timeoutMs = 60_000) =>
        WaitForAsync(runId, r => r.Status == status, timeoutMs);

    /// <summary>Waits until the run's human input is registered (approve/reject can then be called).</summary>
    public async Task WaitForHumanInputAsync(Guid runId, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!HumanInputs.IsPending(runId))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Run {runId} is not awaiting input.");
            }

            await Task.Delay(10);
        }
    }

    public async Task<IReadOnlyList<Contracts.Api.RunEventInfo>> EventsAsync(Guid runId) =>
        (await Store.ListEventsAsync(new EventQuery(RunId: runId))).Reverse().ToList();

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();
}

/// <summary>Serves module packages and tools from memory; a module can be taken down.</summary>
public sealed class FakeModuleClient : IModuleClient
{
    private readonly ConcurrentDictionary<string, ModulePackage> _packages = new();

    public ConcurrentDictionary<string, Func<JsonNode?, JsonNode?>> Tools { get; } = new();

    public ConcurrentQueue<ToolInvocationRequest> ToolCalls { get; } = new();

    public HashSet<string> DownModules { get; } = [];

    public void SetPackage(string moduleId, ModulePackage package) => _packages[moduleId] = package;

    public Task<PackageFetchResult> FetchPackageAsync(string moduleId, string? knownVersion, CancellationToken ct)
    {
        if (DownModules.Contains(moduleId) || !_packages.TryGetValue(moduleId, out var package))
        {
            return Task.FromResult<PackageFetchResult>(new PackageFetchResult.Failed($"No connection could be made to '{moduleId}'."));
        }

        return Task.FromResult<PackageFetchResult>(package.Version == knownVersion
            ? new PackageFetchResult.NotModified()
            : new PackageFetchResult.Changed(package));
    }

    public Task<JsonNode?> InvokeToolAsync(string moduleId, string toolName, ToolInvocationRequest request, TimeSpan timeout, CancellationToken ct)
    {
        ToolCalls.Enqueue(request);
        if (DownModules.Contains(moduleId))
        {
            throw new ToolInvocationException($"Module '{moduleId}' is unavailable: No connection could be made to '{moduleId}'.");
        }

        if (!Tools.TryGetValue(toolName, out var tool))
        {
            throw new ToolInvocationException($"Tool '{toolName}' failed: Unknown tool.");
        }

        try
        {
            return Task.FromResult(tool(request.Input));
        }
        catch (Exception ex) when (ex is not ToolInvocationException)
        {
            throw new ToolInvocationException($"Tool '{toolName}' failed: {ex.Message}");
        }
    }
}
