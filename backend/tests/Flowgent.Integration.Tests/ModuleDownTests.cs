namespace Flowgent.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Fixtures.Module;
using Flowgent.Testing;

/// <summary>
/// The fixture service stops: the engine reports it unavailable, rejects its new inputs, fails its in-flight tool
/// steps with a descriptive reason and keeps the support module working.
/// </summary>
public class ModuleDownTests
{
    [Fact]
    public async Task StoppedModule_IsUnavailableAndRejectsNewInputs()
    {
        await using var platform = await IntegrationPlatform.StartAsync();

        await platform.StopModuleAsync(FixtureHost.ModuleId);

        var modules = await platform.GetAsync<List<ModuleInfo>>("/api/engine/modules");
        var fixture = modules.Single(m => m.Id == FixtureHost.ModuleId);
        Assert.Equal(ModuleStatus.Unavailable, fixture.Status);
        Assert.Contains("fixture", fixture.LastError);
        Assert.Equal(ModuleStatus.Available, modules.Single(m => m.Id == "support").Status);

        var workflow = await platform.GetAsync<WorkflowInfo>($"/api/engine/workflows/{FixtureHost.FlowWorkflowId}");
        Assert.False(workflow.Available);

        var intake = await platform.Client.PostAsJsonAsync(EngineRoutes.SubmitRun(FixtureHost.FlowWorkflowId), new { text = "x", priority = "high" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, intake.StatusCode);
        Assert.Contains("fixture", await intake.Content.ReadAsStringAsync());

        // The gateway answers for the stopped service with 503 problem details naming it.
        var routed = await platform.Client.GetAsync($"/api/modules/{FixtureHost.ModuleId}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, routed.StatusCode);
        using var problem = JsonDocument.Parse(await routed.Content.ReadAsStringAsync());
        Assert.Equal(FixtureHost.ModuleId, problem.RootElement.GetProperty("service").GetString());

        // The support module keeps working end-to-end.
        var supportRun = await platform.SubmitSupportEmailAsync("scenario-a2");
        await platform.WaitForHumanInputAsync(supportRun);
        await platform.ApproveAsync(supportRun);
        var support = await platform.WaitForTerminalAsync(supportRun);
        Assert.Equal((RunStatus.Completed, "code_change_generated"), (support.Status, support.CurrentStepId));
    }

    [Fact]
    public async Task StoppedModule_FailsTheInFlightToolStep()
    {
        // A slow demo LLM keeps the run in its first (agent) step long enough to stop the module before the run reaches
        // its tool steps. The latency is generous so the stop reliably lands during 'draft' even under test-suite load.
        await using var platform = await IntegrationPlatform.StartAsync(o => o.ConfigureEngine("Flowgent:Llm:DemoLatencyMs", "8000"));
        var runId = await platform.SubmitFixtureFlowAsync("in flight");
        await platform.WaitForRunAsync(runId, r => r is { Status: RunStatus.Running, CurrentStepId: "draft" });

        await platform.StopModuleAsync(FixtureHost.ModuleId);
        var run = await platform.WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        // The in-flight run fails at the first module-tool step that hits the stopped service. Both reverse and echo
        // are now module tools reached through InvokeMcpTool (echo was an http step before the MAF migration; the
        // declarative HTTP action is unproven, so the echo "external API" is bridged as fixture.echo). Which of the two
        // is in flight when the stop lands is a timing detail; the business contract is the descriptive failure.
        Assert.Contains(run.CurrentStepId, new[] { "reverse", "echo" });
        Assert.StartsWith("Module 'fixture' is unavailable", run.Detail);
    }

    [Fact]
    public async Task RestartedModule_IsAvailableAgain()
    {
        await using var platform = await IntegrationPlatform.StartAsync();
        await platform.StopModuleAsync(FixtureHost.ModuleId);

        await platform.StartModuleAsync(FixtureHost.ModuleId);

        var fixture = (await platform.GetAsync<List<ModuleInfo>>("/api/engine/modules")).Single(m => m.Id == FixtureHost.ModuleId);
        Assert.Equal(ModuleStatus.Available, fixture.Status);
        var run = await platform.WaitForTerminalAsync(await platform.SubmitFixtureAsync(FixtureHost.RulesWorkflowId, new { text = "alpha" }));
        Assert.Equal(RunStatus.Completed, run.Status);
    }
}
