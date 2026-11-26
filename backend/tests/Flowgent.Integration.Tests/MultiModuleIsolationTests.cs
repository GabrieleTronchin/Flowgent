namespace Flowgent.Integration.Tests;

using System.Net;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Fixtures.Module;
using Flowgent.Modules.Support;
using Flowgent.Testing;

/// <summary>
/// Support and fixture runs execute concurrently on the same engine: state, events, human inputs, telemetry and
/// API listings of one module are not affected by the other.
/// </summary>
public sealed class MultiModuleIsolationTests : IAsyncLifetime
{
    private static readonly string[] SupportSamples = ["scenario-a1", "scenario-b2", "scenario-a3"];
    private const int FixtureRuns = 3;

    private PlatformHarness _platform = null!;

    public async ValueTask InitializeAsync() => _platform = await IntegrationPlatform.StartAsync();

    public async ValueTask DisposeAsync() => await _platform.DisposeAsync();

    [Fact]
    public async Task Catalog_ListsBothModulesAndTheirOwnWorkflows()
    {
        var modules = await _platform.GetAsync<List<ModuleInfo>>("/api/engine/modules");
        var fixtureWorkflows = await _platform.GetAsync<List<WorkflowInfo>>("/api/engine/workflows?moduleId=fixture");
        var supportWorkflows = await _platform.GetAsync<List<WorkflowInfo>>("/api/engine/workflows?moduleId=support");
        var fixtureAgents = await _platform.GetAsync<List<AgentInfo>>("/api/engine/agents?moduleId=fixture");

        Assert.Equal(["support", "fixture"], modules.Select(m => m.Id));
        Assert.All(modules, m => Assert.Equal(ModuleStatus.Available, m.Status));
        Assert.Equal([FixtureHost.FlowWorkflowId, FixtureHost.RulesWorkflowId], fixtureWorkflows.Select(w => w.Id));
        Assert.Equal([SupportHost.WorkflowId], supportWorkflows.Select(w => w.Id));
        Assert.Equal(["fixture_writer", "fixture_reviewer"], fixtureAgents.Select(a => a.AgentId));
    }

    // TODO(flaky): samples the transient agent "Working" state at one instant under concurrent load;
    // flaps on CI. Re-enable after asserting on terminal state or adding a WaitForAgentStatus helper.
    [Fact(Skip = "Flaky: transient agent-Working snapshot. See TODO.")]
    public async Task ConcurrentRuns_StayIsolatedPerModule()
    {
        // Submit both modules' runs at the same time.
        var supportTasks = SupportSamples.Select(_platform.SubmitSupportEmailAsync).ToList();
        var fixtureTasks = Enumerable.Range(0, FixtureRuns).Select(i => _platform.SubmitFixtureFlowAsync($"text {i}", i % 2 == 0 ? "high" : "low")).ToList();
        var supportRuns = await Task.WhenAll(supportTasks);
        var fixtureRuns = await Task.WhenAll(fixtureTasks);

        await Task.WhenAll(supportRuns.Concat(fixtureRuns).Select(id => _platform.WaitForHumanInputAsync(id)));

        // Human inputs, runs and events are listed per module.
        var fixtureInputs = await _platform.GetAsync<List<HumanInputInfo>>("/api/engine/human-inputs?moduleId=fixture");
        var supportInputs = await _platform.GetAsync<List<HumanInputInfo>>("/api/engine/human-inputs?moduleId=support");
        Assert.Equal(fixtureRuns.Order(), fixtureInputs.Select(i => i.RunId).Order());
        Assert.Equal(supportRuns.Order(), supportInputs.Select(i => i.RunId).Order());
        Assert.All(fixtureInputs, i => Assert.Equal(("fixture", FixtureHost.FlowWorkflowId, "review"), (i.ModuleId, i.WorkflowId, i.StepId)));
        Assert.All(supportInputs, i => Assert.Equal(("support", SupportHost.WorkflowId, "approval"), (i.ModuleId, i.WorkflowId, i.StepId)));

        var workingFixtureAgents = (await _platform.GetAsync<List<AgentInfo>>("/api/engine/agents?moduleId=fixture")).Where(a => a.Status == "Working").ToList();
        Assert.Equal(["fixture_reviewer"], workingFixtureAgents.Select(a => a.AgentId));
        Assert.Contains(workingFixtureAgents[0].CurrentRunId!.Value, fixtureRuns);

        foreach (var id in supportRuns.Concat(fixtureRuns))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await _platform.ApproveAsync(id)).StatusCode);
        }

        var completedSupport = await Task.WhenAll(supportRuns.Select(_platform.WaitForTerminalAsync));
        var completedFixture = await Task.WhenAll(fixtureRuns.Select(_platform.WaitForTerminalAsync));

        // Each run only holds the outputs of its own workflow's steps.
        Assert.All(completedSupport, run =>
        {
            Assert.Equal((RunStatus.Completed, "code_change_generated", "support"), (run.Status, run.CurrentStepId, run.ModuleId));
            Assert.DoesNotContain("draft", run.StepOutputs.Select(p => p.Key));
        });
        Assert.All(completedFixture, run =>
        {
            Assert.Equal((RunStatus.Completed, "done", "fixture"), (run.Status, run.CurrentStepId, run.ModuleId));
            Assert.Equal(run.Id.ToString(), run.FinalOutput!["runHeader"]!.GetValue<string>());
            Assert.Equal(new string([.. $"draft {run.Input!["text"]!.GetValue<string>()}".Reverse()]), run.Detail);
            Assert.DoesNotContain("classify", run.StepOutputs.Select(p => p.Key));
        });

        var supportList = await _platform.GetAsync<List<RunSummary>>("/api/engine/runs?moduleId=support");
        var fixtureList = await _platform.GetAsync<List<RunSummary>>("/api/engine/runs?moduleId=fixture");
        Assert.Equal(supportRuns.Order(), supportList.Select(r => r.Id).Order());
        Assert.Equal(fixtureRuns.Order(), fixtureList.Select(r => r.Id).Order());

        var fixtureEvents = await _platform.GetAsync<List<RunEventInfo>>("/api/engine/events?moduleId=fixture");
        var supportEvents = await _platform.GetAsync<List<RunEventInfo>>("/api/engine/events?moduleId=support");
        Assert.All(fixtureEvents, e => Assert.Contains(e.RunId, fixtureRuns));
        Assert.All(supportEvents, e => Assert.Contains(e.RunId, supportRuns));
        Assert.Equal(FixtureRuns, fixtureEvents.Count(e => e.Kind == RunEventKind.Completed));
        Assert.Equal(SupportSamples.Length, supportEvents.Count(e => e.Kind == RunEventKind.Completed));

        // Telemetry: support makes 3 LLM calls per approved run, the fixture 1 (attributed to its writer).
        var supportTelemetry = await _platform.GetAsync<TelemetrySummary>("/api/engine/telemetry/summary?moduleId=support");
        var fixtureTelemetry = await _platform.GetAsync<TelemetrySummary>("/api/engine/telemetry/summary?moduleId=fixture");
        var writer = await _platform.GetAsync<AgentTelemetry>("/api/engine/agents/fixture_writer/telemetry");
        Assert.Equal(3 * SupportSamples.Length, supportTelemetry.TotalCalls);
        Assert.Equal(FixtureRuns, fixtureTelemetry.TotalCalls);
        Assert.Equal(FixtureRuns, writer.TotalCalls);
        Assert.Equal("fixture", writer.LastCall!.ModuleId);
    }

    [Fact]
    public async Task AbortingAFixtureRun_DoesNotAffectASupportRun()
    {
        var supportRun = await _platform.SubmitSupportEmailAsync("scenario-b1");
        var fixtureRun = await _platform.SubmitFixtureFlowAsync("to abort");
        await _platform.WaitForHumanInputAsync(supportRun);
        await _platform.WaitForHumanInputAsync(fixtureRun);

        Assert.Equal(HttpStatusCode.Accepted, (await _platform.AbortAsync(fixtureRun)).StatusCode);
        var aborted = await _platform.WaitForTerminalAsync(fixtureRun);

        var support = await _platform.GetRunAsync(supportRun);
        Assert.Equal((RunStatus.Failed, "Aborted by user"), (aborted.Status, aborted.Detail));
        Assert.Equal((RunStatus.AwaitingInput, "approval"), (support.Status, support.CurrentStepId));
        Assert.Single(await _platform.GetAsync<List<HumanInputInfo>>("/api/engine/human-inputs"), i => i.RunId == supportRun);
    }
}
