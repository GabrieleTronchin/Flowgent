namespace Flowgent.Engine.Tests.Helpers;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Service;
using Flowgent.Runtime.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>The engine service on a TestServer, with an in-memory module client and Demo mode.</summary>
public sealed class EngineTestHost : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private EngineTestHost(WebApplication app, FakeModuleClient modules)
    {
        App = app;
        Modules = modules;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public FakeModuleClient Modules { get; }

    public IServiceProvider Services => App.Services;

    public static async Task<EngineTestHost> StartAsync(
        Action<Dictionary<string, string>>? patch = null,
        IDictionary<string, string?>? configuration = null,
        int demoLatencyMs = 0)
    {
        var modules = new FakeModuleClient();
        modules.SetPackage("demo", TestModules.Package(patch));
        modules.Tools["demo.count"] = input => new JsonObject { ["words"] = input?["text"]?.GetValue<string>().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0 };

        var settings = new Dictionary<string, string?>
        {
            ["Flowgent:Modules:0:Id"] = "demo",
            ["Flowgent:Modules:0:Url"] = "http://demo",
            ["Flowgent:Modules:0:Retries"] = "0",
            ["Flowgent:Modules:0:RefreshSeconds"] = "0.1",
            ["Flowgent:InboxPollingIntervalSeconds"] = "0.05",
            ["Flowgent:Llm:Provider"] = "Demo",
            ["Flowgent:Llm:DemoLatencyMs"] = demoLatencyMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var (key, value) in configuration ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var app = EngineHost.Create(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory }, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddSingleton<IModuleClient>(modules);
        });
        await app.StartAsync();
        await app.Services.GetRequiredService<ModuleCatalogService>().RefreshAllAsync();
        return new EngineTestHost(app, modules);
    }

    public async Task<IntakeAccepted> SubmitAsync(object input, string workflowId = "hello")
    {
        var response = await Client.PostAsJsonAsync(EngineRoutes.SubmitRun(workflowId), input, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IntakeAccepted>(Json))!;
    }

    public async Task<RunDetail> GetRunAsync(Guid runId) =>
        (await Client.GetFromJsonAsync<RunDetail>(EngineRoutes.Run(runId), Json))!;

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

    public Task<RunDetail> WaitForStatusAsync(Guid runId, RunStatus status) => WaitForRunAsync(runId, r => r.Status == status);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
