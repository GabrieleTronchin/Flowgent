namespace Flowgent.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using Flowgent.Contracts.Api;
using Flowgent.Fixtures.Module;
using Flowgent.Modules.Template;
using Flowgent.Testing;

/// <summary>
/// Gateway + engine + the test fixture module + the module template, all in-process (Demo mode). Only core projects:
/// the samples have their own platform tests in samples/tests.
/// </summary>
public static class IntegrationPlatform
{
    public static readonly string FixtureContentRoot = Path.Combine("tests", "Fixtures", "Flowgent.Fixtures.Module");
    public static readonly string TemplateContentRoot = Path.Combine("template", "service");

    public static string FixtureDefinitionFolder => Path.Combine(PlatformHarness.RepositoryRoot, FixtureContentRoot, "definition");

    public static Task<PlatformHarness> StartAsync(Action<PlatformOptions>? configure = null) =>
        PlatformHarness.StartAsync(options =>
        {
            options.AddModule(FixtureHost.ModuleId, FixtureHost.Create, FixtureContentRoot);
            options.AddModule(TemplateHost.ModuleId, TemplateHost.Create, TemplateContentRoot);
            options.WithGateway();
            configure?.Invoke(options);
        });

    /// <summary>Submits a run of a fixture workflow through the gateway.</summary>
    public static async Task<Guid> SubmitFixtureAsync(this PlatformHarness platform, string workflowId, object input)
    {
        var response = await platform.Client.PostAsJsonAsync(EngineRoutes.SubmitRun(workflowId), input, PlatformHarness.Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IntakeAccepted>(PlatformHarness.Json))!.RunId;
    }

    public static Task<Guid> SubmitFixtureFlowAsync(this PlatformHarness platform, string text, string priority = "high") =>
        platform.SubmitFixtureAsync(FixtureHost.FlowWorkflowId, new { text, priority });

    /// <summary>Submits a run of the template workflow (no human input: it ends on its own).</summary>
    public static Task<Guid> SubmitTemplateAsync(this PlatformHarness platform, string text) =>
        platform.SubmitFixtureAsync(TemplateHost.WorkflowId, new { text });

    public static async Task<T> GetAsync<T>(this PlatformHarness platform, string path) =>
        (await platform.Client.GetFromJsonAsync<T>(path, PlatformHarness.Json))!;
}
