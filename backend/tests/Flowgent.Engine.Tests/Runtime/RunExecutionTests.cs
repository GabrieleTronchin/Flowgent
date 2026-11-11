namespace Flowgent.Engine.Tests.Runtime;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Tests.Helpers;
using Flowgent.Runtime.Llm;

public class RunExecutionTests
{
    [Fact]
    public async Task DemoRun_ExecutesAllStepsAndCompletesOnSuccessTerminal()
    {
        await using var engine = await TestEngine.StartAsync();

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("long", run.CurrentStepId);
        Assert.Equal("[Demo] Agent Framework workflows in YAML", run.Detail);
        Assert.Equal("[Demo] Agent Framework workflows in YAML", run.StepOutputs["summarize"]!["summary"]!.GetValue<string>());
        Assert.Equal(6, run.StepOutputs["count"]!["words"]!.GetValue<int>());
        Assert.Equal("[Demo] Agent Framework workflows in YAML", run.FinalOutput!["summary"]!.GetValue<string>());
        Assert.Equal("Agent Framework workflows in YAML", run.Title);
    }

    [Fact]
    public async Task DemoRun_RecordsEventsInOrder()
    {
        await using var engine = await TestEngine.StartAsync();

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });
        var events = await engine.EventsAsync(run.Id);

        Assert.Equal(
            [
                (RunEventKind.Queued, (string?)null),
                (RunEventKind.StepStarted, "summarize"),
                (RunEventKind.StepCompleted, "summarize"),
                (RunEventKind.StepStarted, "count"),
                (RunEventKind.StepCompleted, "count"),
                (RunEventKind.Completed, "long"),
            ],
            events.Select(e => (e.Kind, e.StepId)));
        Assert.Equal("summarize", events[3].PreviousStepId);
        Assert.Equal(RunStatus.Completed, events[^1].Status);
    }

    [Fact]
    public async Task FailureTerminal_EndsRunAsFailedWithMessage()
    {
        await using var engine = await TestEngine.StartAsync();

        var run = await engine.RunAsync(new JsonObject { ["text"] = "short" });

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("short", run.CurrentStepId);
        Assert.Equal("Too short", run.Detail);
    }

    [Fact]
    public async Task LlmCall_IsAttributedToTheStepAgentInTelemetry()
    {
        await using var engine = await TestEngine.StartAsync();

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });

        var call = Assert.Single(engine.Telemetry.GetCalls());
        Assert.Equal("demo_writer", call.AgentId);
        Assert.Equal("demo", call.ModuleId);
        Assert.Equal("hello", call.WorkflowId);
        Assert.Equal(run.Id, call.RunId);
        Assert.Equal("summarize", call.StepId);
        Assert.Equal(DemoChatClient.DemoModelId, call.ModelName);
        Assert.True(call.Success);
        Assert.True(call.PromptTokens > 0);
    }

    [Fact]
    public async Task ToolFailure_FailsTheRunAtTheToolStep()
    {
        await using var engine = await TestEngine.StartAsync();
        engine.Modules.Tools["demo.count"] = _ => throw new InvalidOperationException("boom");

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows" });

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("count", run.CurrentStepId);
        Assert.Equal("Tool 'demo.count' failed: boom", run.Detail);
    }

    [Fact]
    public async Task ToolOutputNotMatchingSchema_FailsTheRun()
    {
        await using var engine = await TestEngine.StartAsync();
        engine.Modules.Tools["demo.count"] = _ => new JsonObject { ["words"] = "many" };

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows" });

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("Output of tool 'demo.count' does not match its schema", run.Detail);
    }

    [Fact]
    public async Task ModuleDown_FailsInFlightToolStepWithDescriptiveReason()
    {
        await using var engine = await TestEngine.StartAsync();
        engine.Modules.DownModules.Add("demo");

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows" });

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("count", run.CurrentStepId);
        Assert.StartsWith("Module 'demo' is unavailable", run.Detail);
    }

    [Fact]
    public async Task ToolInvocation_CarriesRunContext()
    {
        await using var engine = await TestEngine.StartAsync();

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows" });

        var call = Assert.Single(engine.Modules.ToolCalls);
        Assert.Equal(run.Id, call.RunId);
        Assert.Equal("demo", call.ModuleId);
        Assert.Equal("hello", call.WorkflowId);
        // Observability change (documented): MAF InvokeMcpTool does not surface the logical step id to the seam, so the
        // ToolInvocationRequest.StepId carries the tool name rather than the workflow step id (the run's CurrentStepId
        // still resolves to the step "count"). The tool input is unchanged.
        Assert.Equal("demo.count", call.StepId);
        Assert.Equal("[Demo] Agent Framework workflows", call.Input!["text"]!.GetValue<string>());
    }
}
