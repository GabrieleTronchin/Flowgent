namespace Flowgent.Integration.Tests;

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

/// <summary>Gateway + engine + support module + fixture module, all in-process (Demo mode).</summary>
public static class IntegrationPlatform
{
    public static readonly string FixtureContentRoot = Path.Combine("tests", "Fixtures", "Flowgent.Fixtures.Module");
    public static readonly string SupportContentRoot = Path.Combine("modules", "support", "service");

    private static readonly Lazy<Definitions.ModuleDefinition> LazySupport = new(() =>
        ModuleDefinitionAssert.IsValid(Path.Combine(PlatformHarness.RepositoryRoot, "modules", "support", "definition")));

    public static string FixtureDefinitionFolder => Path.Combine(PlatformHarness.RepositoryRoot, FixtureContentRoot, "definition");

    public static SampleDefinition SupportSample(string id) => LazySupport.Value.Samples.Single(s => s.Id == id);

    public static Task<PlatformHarness> StartAsync(Action<PlatformOptions>? configure = null) =>
        PlatformHarness.StartAsync(options =>
        {
            options.AddModule(SupportHost.ModuleId, SupportHost.Create, SupportContentRoot, "/api/support/{**catch-all}");
            options.AddMcpServer(SupportHost.ModuleId, SupportMcpHost.Create, Path.Combine("modules", "support", "mcp", "Flowgent.Mcp.Support"));
            options.ConfigureModule(SupportHost.ModuleId, SupportMcpOptions.EnvironmentVariable, Path.Combine(PlatformHarness.RepositoryRoot, "modules", "support", "dummy-apps"));
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

    /// <summary>Submits a run of a fixture workflow through the gateway.</summary>
    public static async Task<Guid> SubmitFixtureAsync(this PlatformHarness platform, string workflowId, object input)
    {
        var response = await platform.Client.PostAsJsonAsync(EngineRoutes.SubmitRun(workflowId), input, PlatformHarness.Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IntakeAccepted>(PlatformHarness.Json))!.RunId;
    }

    public static Task<Guid> SubmitFixtureFlowAsync(this PlatformHarness platform, string text, string priority = "high") =>
        platform.SubmitFixtureAsync(FixtureHost.FlowWorkflowId, new { text, priority });

    public static async Task<T> GetAsync<T>(this PlatformHarness platform, string path) =>
        (await platform.Client.GetFromJsonAsync<T>(path, PlatformHarness.Json))!;
}
