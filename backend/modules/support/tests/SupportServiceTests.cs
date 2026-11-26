namespace Flowgent.Modules.Support.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Modules;
using Flowgent.Testing;

/// <summary>Support service endpoints, SDK contract and the generated <c>support.http</c>.</summary>
public class SupportServiceTests
{
    [Theory]
    [InlineData("", "body")]
    [InlineData("   ", "body")]
    [InlineData("subject", "")]
    [InlineData("subject", "  ")]
    [InlineData(null, "body")]
    [InlineData("subject", null)]
    public async Task Email_BlankSubjectOrBody_Returns400(string? subject, string? body)
    {
        await using var platform = await SupportPlatform.StartAsync();

        var response = await platform.Client.PostAsJsonAsync("/api/support/emails", new { sender = "a@b.c", subject, body });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Subject and Body are required.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Email_EngineDown_Returns503()
    {
        await using var platform = await SupportPlatform.StartAsync();
        platform.Router.SetDown("engine");

        var response = await platform.Client.PostAsJsonAsync("/api/support/emails", SupportPlatform.Sample("scenario-a1").Input);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("engine is unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Email_SupportModuleDown_GatewayReturns503()
    {
        await using var platform = await SupportPlatform.StartAsync();
        platform.Router.SetDown("support");

        var response = await platform.Client.PostAsJsonAsync("/api/support/emails", SupportPlatform.Sample("scenario-a1").Input);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("'support'", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Package_IsServedWithETagAndNotModified()
    {
        await using var platform = await SupportPlatform.StartAsync();
        var client = platform.ModuleClient("support");

        var response = await client.GetAsync(ModuleRoutes.Package);
        var package = await response.Content.ReadFromJsonAsync<ModulePackage>(PlatformHarness.Json);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, ModuleRoutes.Package);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag!.Tag);
        var notModified = await client.SendAsync(conditional);

        Assert.Equal("support", package!.ModuleId);
        Assert.Contains("workflows/support-email.yaml", package.Files.Keys);
        Assert.Equal($"\"{package.Version}\"", response.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task UnknownTool_Returns404WithError()
    {
        await using var platform = await SupportPlatform.StartAsync();

        var response = await platform.ModuleClient("support").PostAsJsonAsync(
            ModuleRoutes.Tool("support.ghost"),
            new ToolInvocationRequest(Guid.NewGuid(), "support", "support-email", "x", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Unknown tool", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ModuleSummary_IsRoutedByTheGateway()
    {
        await using var platform = await SupportPlatform.StartAsync();

        var summary = JsonNode.Parse(await platform.Client.GetStringAsync("/api/modules/support"))!;

        Assert.Equal("support", summary["id"]!.GetValue<string>());
        Assert.True(summary["valid"]!.GetValue<bool>());
        Assert.Equal("support-email", summary["workflows"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Engine_ReportsTheSupportModuleAvailable()
    {
        await using var platform = await SupportPlatform.StartAsync();

        var module = Assert.Single((await platform.Client.GetFromJsonAsync<List<ModuleInfo>>("/api/engine/modules", PlatformHarness.Json))!);

        Assert.Equal(ModuleStatus.Available, module.Status);
        Assert.Empty(module.Errors);
    }

    /// <summary>
    /// <c>service/support.http</c> is generated from <c>samples/scenarios.yaml</c>, so requests and dashboard
    /// templates cannot drift. Set <c>UPDATE_SNAPSHOTS=1</c> to regenerate the file.
    /// </summary>
    [Fact]
    public void HttpFile_IsGeneratedFromTheSamples()
    {
        var path = Path.Combine(SupportPlatform.ModuleFolder, "service", "support.http");
        var expected = GenerateHttpFile();

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path), "support.http is missing; run the tests with UPDATE_SNAPSHOTS=1.");
        Assert.Equal(expected, File.ReadAllText(path).Replace("\r\n", "\n"));
    }

    internal static string GenerateHttpFile()
    {
        var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var text = new StringBuilder();
        text.Append("# Support module requests, generated from definition/samples/scenarios.yaml (do not edit by hand).\n");
        text.Append("# Regenerate: set UPDATE_SNAPSHOTS=1 and run the support module tests.\n");
        text.Append("# Requests go through the gateway (start everything with the AppHost).\n\n");
        text.Append("@HostAddress = http://localhost:5080\n\n");

        foreach (var sample in SupportPlatform.Samples)
        {
            text.Append($"### {sample.Name} ({sample.Group}){(sample.Description is null ? "" : " - " + sample.Description)}\n\n");
            text.Append("POST {{HostAddress}}/api/support/emails\n");
            text.Append("Content-Type: application/json\n\n");
            text.Append(sample.Input!.ToJsonString(json).Replace("\r\n", "\n")).Append("\n\n");
        }

        text.Append("### Runs of the support workflow\n\nGET {{HostAddress}}/api/engine/runs?workflowId=support-email\n\n");
        text.Append("### Runs awaiting approval\n\nGET {{HostAddress}}/api/engine/human-inputs?workflowId=support-email\n\n");
        text.Append("### Event log of the support module\n\nGET {{HostAddress}}/api/engine/events?moduleId=support\n\n");
        text.Append("### Support agents\n\nGET {{HostAddress}}/api/engine/agents?moduleId=support\n");
        return text.ToString();
    }
}
