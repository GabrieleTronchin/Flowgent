namespace Flowgent.Modules.Template.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Module.Sdk;
using Flowgent.Modules.Template.Tools;
using Flowgent.Testing;

/// <summary>The template definition is valid and its workflow runs end-to-end in Demo mode through the gateway.</summary>
public class TemplateModuleTests
{
    private static readonly string ModuleFolder = Path.Combine(PlatformHarness.RepositoryRoot, "modules", "_template");

    [Fact]
    public void Definition_IsValidWithoutWarnings()
    {
        var module = ModuleDefinitionAssert.IsValid(Path.Combine(ModuleFolder, "definition"));

        Assert.Equal(TemplateHost.ModuleId, module.Id);
        Assert.Empty(module.Warnings);
        Assert.NotNull(module.FindWorkflow(TemplateHost.WorkflowId));
        Assert.Single(module.Samples);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one", 1)]
    [InlineData(" two  words ", 2)]
    [InlineData("line one\nline\ttwo", 4)]
    public void WordCount_CountsWhitespaceSeparatedWords(string text, int expected) =>
        Assert.Equal(expected, WordCountTool.Count(text));

    [Fact]
    public async Task Workflow_RunsEndToEndInDemoMode()
    {
        await using var platform = await StartAsync();
        var sample = (await platform.Client.GetFromJsonAsync<WorkflowInfo>(
            $"{EngineRoutes.Base}/workflows/{TemplateHost.WorkflowId}", PlatformHarness.Json))!.Samples.Single();

        var response = await platform.Client.PostAsJsonAsync(EngineRoutes.SubmitRun(TemplateHost.WorkflowId), sample.Input);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<IntakeAccepted>(PlatformHarness.Json))!;
        var run = await platform.WaitForTerminalAsync(accepted.RunId);

        var summary = run.StepOutputs["summarize"]!["summary"]!.GetValue<string>();
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("done", run.CurrentStepId);
        Assert.StartsWith("[Demo] ", summary);
        Assert.Equal(WordCountTool.Count(summary), run.FinalOutput!["words"]!.GetValue<int>());
        Assert.Equal($"{summary} ({WordCountTool.Count(summary)} words)", run.Detail);
    }

    [Fact]
    public async Task BlankText_IsRejectedWithTheWorkflowErrorMessage()
    {
        await using var platform = await StartAsync();

        var response = await platform.Client.PostAsJsonAsync(EngineRoutes.SubmitRun(TemplateHost.WorkflowId), new { text = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Text is required.", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task ModuleSummary_IsRoutedByTheGateway()
    {
        await using var platform = await StartAsync();

        var summary = JsonNode.Parse(await platform.Client.GetStringAsync($"/api/modules/{TemplateHost.ModuleId}"))!;

        Assert.True(summary["valid"]!.GetValue<bool>());
        Assert.Equal(TemplateHost.WorkflowId, summary["workflows"]![0]!.GetValue<string>());
    }

    private static Task<PlatformHarness> StartAsync() =>
        PlatformHarness.StartAsync(options => options
            .AddModule(TemplateHost.ModuleId, TemplateHost.Create, Path.Combine("modules", "_template", "service"))
            .WithGateway());
}
