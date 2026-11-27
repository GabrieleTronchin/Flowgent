namespace Flowgent.Samples.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Definitions.Model;
using Flowgent.Fixtures.Module;
using Flowgent.Mcp.Support;
using Flowgent.Module.Sdk;
using Flowgent.Modules.Support;
using Flowgent.Testing;

/// <summary>Gateway + engine + the support sample (with its MCP server) + the core fixture module, all in-process (Demo mode).</summary>
public static class SamplesPlatform
{
    private static readonly string FixtureContentRoot = Path.Combine("tests", "Fixtures", "Flowgent.Fixtures.Module");

    private static readonly Lazy<Definitions.ModuleDefinition> LazySupport = new(() =>
        ModuleDefinitionAssert.IsValid(SamplesPaths.Sample("support", "definition")));

    public static SampleDefinition SupportSample(string id) => LazySupport.Value.Samples.Single(s => s.Id == id);

    public static Task<PlatformHarness> StartAsync(Action<PlatformOptions>? configure = null) =>
        PlatformHarness.StartAsync(options =>
        {
            options.AddModule(SupportHost.ModuleId, SupportHost.Create, SamplesPaths.Sample("support", "service"), "/api/support/{**catch-all}");
            options.AddMcpServer(SupportHost.ModuleId, SupportMcpHost.Create, SamplesPaths.Sample("support", "mcp", "Flowgent.Mcp.Support"));
            options.ConfigureModule(SupportHost.ModuleId, SupportMcpOptions.EnvironmentVariable, SamplesPaths.Sample("support", "dummy-apps"));
            options.AddModule(FixtureHost.ModuleId, FixtureHost.Create, FixtureContentRoot);
            options.WithGateway();
            configure?.Invoke(options);
        });

    /// <summary>Submits a support sample through the support module's legacy email endpoint (via the gateway).</summary>
    public static async Task<Guid> SubmitSupportEmailAsync(this PlatformHarness platform, string sampleId)
    {
        var response = await platform.Client.PostAsJsonAsync("/api/support/emails", SupportSample(sampleId).Input);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Guid.Parse(JsonNode.Parse(await response.Content.ReadAsStringAsync())!["runId"]!.GetValue<string>());
    }

    /// <summary>Submits a run of the fixture flow workflow through the gateway.</summary>
    public static async Task<Guid> SubmitFixtureFlowAsync(this PlatformHarness platform, string text, string priority = "high")
    {
        var response = await platform.Client.PostAsJsonAsync(EngineRoutes.SubmitRun(FixtureHost.FlowWorkflowId), new { text, priority }, PlatformHarness.Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IntakeAccepted>(PlatformHarness.Json))!.RunId;
    }

    public static async Task<T> GetAsync<T>(this PlatformHarness platform, string path) =>
        (await platform.Client.GetFromJsonAsync<T>(path, PlatformHarness.Json))!;
}
