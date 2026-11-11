namespace Flowgent.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Definitions.Model;
using Flowgent.Fixtures.Module;
using Flowgent.Module.Sdk;
using Flowgent.Testing;

/// <summary>The fixture module's workflows run through the gateway: every step kind and both terminal outcomes.</summary>
public sealed class FixtureModuleTests : IAsyncLifetime
{
    private PlatformHarness _platform = null!;

    public async ValueTask InitializeAsync() => _platform = await IntegrationPlatform.StartAsync();

    public async ValueTask DisposeAsync() => await _platform.DisposeAsync();

    [Fact]
    public void Definition_IsValidAndCoversEveryStepKind()
    {
        var module = ModuleDefinitionAssert.IsValid(IntegrationPlatform.FixtureDefinitionFolder);

        Assert.Empty(module.Warnings);
        // The declarative workflow document is carried as raw YAML; assert step-kind coverage over the text.
        var yaml = string.Concat(module.Workflows.Select(w => w.Yaml));
        Assert.All(
            new[] { StepKinds.Http, StepKinds.HumanInput, StepKinds.Llm, StepKinds.Map, StepKinds.Rule, StepKinds.Terminal, StepKinds.Tool },
            kind => Assert.Contains($"kind: {kind}", yaml));
        // Both terminal outcomes are declared in the manifest (projected onto the graph nodes).
        Assert.Equal(
            ["success", "failure"],
            module.FindWorkflow(FixtureHost.FlowWorkflowId)!.Graph.Nodes
                .Where(n => n.Outcome is not null)
                .Select(n => n.Outcome)
                .Distinct());
    }

    [Fact]
    public async Task Flow_Approved_RunsEveryStepAndSucceeds()
    {
        var runId = await _platform.SubmitFixtureFlowAsync("hello fixture");

        var input = await _platform.WaitForHumanInputAsync(runId);
        Assert.Equal("Review hello fixture", input.Review.Title);
        Assert.Equal("fast-lane", input.Review.Fields.Single(f => f.Label == "Queue").Value);
        Assert.Equal(HttpStatusCode.NoContent, (await _platform.ApproveAsync(runId)).StatusCode);
        var run = await _platform.WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("done", run.CurrentStepId);
        Assert.Equal("fixture_reviewer", run.AssignedAgentId);
        Assert.Equal("draft hello fixture", run.StepOutputs["draft"]!["text"]!.GetValue<string>());
        Assert.Equal("fast-lane:DRAFT HELLO FIXTURE", run.StepOutputs["shape"]!["label"]!.GetValue<string>());
        Assert.Equal("erutxif olleh tfard", run.StepOutputs["reverse"]!["reversed"]!.GetValue<string>());
        Assert.Equal("erutxif olleh tfard", run.Detail);
        Assert.Equal(runId.ToString(), run.FinalOutput!["runHeader"]!.GetValue<string>());
        Assert.Equal("fast-lane", run.FinalOutput["queue"]!.GetValue<string>());

        var events = await _platform.GetEventsAsync(runId);
        Assert.Equal(
            ["draft", "shape", "reverse", "echo", "review"],
            events.Where(e => e.Kind == RunEventKind.StepCompleted).Select(e => e.StepId!));
        Assert.Equal(RunEventKind.Completed, events[^1].Kind);
    }

    [Fact]
    public async Task Flow_Rejected_EndsWithTheFailureOutcome()
    {
        var runId = await _platform.SubmitFixtureFlowAsync("reject me", "low");
        await _platform.WaitForHumanInputAsync(runId);

        await _platform.RejectAsync(runId, "Not good enough");
        var run = await _platform.WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("declined", run.CurrentStepId);
        Assert.Equal("Not good enough", run.Detail);
        Assert.Equal("slow-lane", run.StepOutputs["shape"]!["queue"]!.GetValue<string>());
    }

    [Fact]
    public async Task Flow_InvalidInput_IsRejectedWithTheWorkflowErrorMessage()
    {
        var response = await _platform.Client.PostAsJsonAsync(EngineRoutes.SubmitRun(FixtureHost.FlowWorkflowId), new { text = "x", priority = "urgent" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Text and priority are required.", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("route alpha please", "Alpha")]
    [InlineData("BETA only", "Beta")]
    [InlineData("nothing to see", "Other")]
    public async Task Rules_UseTheMatchingRuleOrTheFallbackOutput(string text, string group)
    {
        var run = await _platform.WaitForTerminalAsync(await _platform.SubmitFixtureAsync(FixtureHost.RulesWorkflowId, new { text }));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("routed", run.CurrentStepId);
        Assert.Equal(group, run.Detail);
        Assert.Equal(group, run.FinalOutput!["group"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rules_SeveralMatches_FailWithTheDeclaredMessage()
    {
        var run = await _platform.WaitForTerminalAsync(await _platform.SubmitFixtureAsync(FixtureHost.RulesWorkflowId, new { text = "alpha and beta" }));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("classify", run.CurrentStepId);
        Assert.Equal("Ambiguous: 'alpha and beta' matches several groups.", run.Detail);
    }
}
