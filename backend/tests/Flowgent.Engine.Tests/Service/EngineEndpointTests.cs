namespace Flowgent.Engine.Tests.Service;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Tests.Helpers;

public class EngineEndpointTests
{
    private static readonly JsonSerializerOptions Json = EngineTestHost.Json;

    [Fact]
    public async Task Config_ReportsDemoModeWithoutSecrets()
    {
        await using var host = await EngineTestHost.StartAsync(configuration: new Dictionary<string, string?> { ["Flowgent:Llm:ApiKey"] = "sk-secret" });

        var text = await host.Client.GetStringAsync("/api/engine/config");
        var config = JsonSerializer.Deserialize<RuntimeConfigInfo>(text, Json)!;

        Assert.True(config.DemoMode);
        Assert.Equal("Demo", config.LlmProvider);
        Assert.Equal("demo-simulated", config.LlmModel);
        Assert.DoesNotContain("sk-secret", text);
    }

    [Fact]
    public async Task Modules_ListsAvailabilityAndWarnings()
    {
        await using var host = await EngineTestHost.StartAsync();

        var modules = await host.Client.GetFromJsonAsync<List<ModuleInfo>>("/api/engine/modules", Json);

        var module = Assert.Single(modules!);
        Assert.Equal("demo", module.Id);
        Assert.Equal("Demo", module.Name);
        Assert.Equal(ModuleStatus.Available, module.Status);
        Assert.Equal(["hello"], module.Workflows);
    }

    [Fact]
    public async Task Modules_InvalidModuleShowsValidationErrors()
    {
        // A malformed declarative workflow is reported by the MAF trial build (design §4.3): a GotoAction to a
        // non-existent action id makes DeclarativeWorkflowBuilder.Build throw. The error names the workflow file; the
        // builder gives no step id or line (documented behavior change from the custom validator).
        await using var host = await EngineTestHost.StartAsync(f => f.Replace("workflows/hello.yaml", "actionId: long_final", "actionId: no_such_action"));

        var module = Assert.Single((await host.Client.GetFromJsonAsync<List<ModuleInfo>>("/api/engine/modules", Json))!);

        Assert.Equal(ModuleStatus.Invalid, module.Status);
        var error = module.Errors.First(e => e.Severity == DefinitionSeverity.Error && e.File == "workflows/hello.yaml");
        Assert.Equal("workflows/hello.yaml", error.File);
    }

    [Fact]
    public async Task Workflows_ExposeGraphInputSchemaAndSamples()
    {
        await using var host = await EngineTestHost.StartAsync();

        var workflow = Assert.Single((await host.Client.GetFromJsonAsync<List<WorkflowInfo>>("/api/engine/workflows?moduleId=demo", Json))!);

        Assert.Equal("hello", workflow.Id);
        Assert.True(workflow.Available);
        Assert.Equal("summarize", workflow.Graph.Start);
        // The declarative projector surfaces the snapshot steps (summarize, count) plus the manifest terminals
        // (long, short), and connects the pipeline steps sequentially. Branch-condition edge labels are no longer
        // projected from the YAML (documented behavior change: routing is PowerFx inside ConditionGroup actions).
        Assert.Equal(["summarize", "count", "long", "short"], workflow.Graph.Nodes.Select(n => n.Id));
        Assert.Equal("success", workflow.Graph.Nodes.Single(n => n.Id == "long").Outcome);
        Assert.Equal("failure", workflow.Graph.Nodes.Single(n => n.Id == "short").Outcome);
        Assert.Contains(workflow.Graph.Edges, e => e is { From: "summarize", To: "count" });
        Assert.Equal("object", workflow.InputSchema!["type"]!.GetValue<string>());
        Assert.Equal(["s1", "empty"], workflow.Samples.Select(s => s.Id));
        Assert.True(workflow.Samples[1].ExpectInvalid);

        Assert.Empty((await host.Client.GetFromJsonAsync<List<WorkflowInfo>>("/api/engine/workflows?moduleId=other", Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/engine/workflows/ghost")).StatusCode);
        Assert.Equal("hello", (await host.Client.GetFromJsonAsync<WorkflowInfo>("/api/engine/workflows/hello", Json))!.Id);
    }

    [Fact]
    public async Task SubmitRun_Accepted_ReturnsIdsAndLocation_AndTheInboxRunsIt()
    {
        await using var host = await EngineTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/engine/workflows/hello/runs", new { text = "Agent Framework workflows in YAML" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<IntakeAccepted>(Json))!;
        Assert.Equal($"/api/engine/runs/{accepted.RunId}", response.Headers.Location!.OriginalString);
        var run = await host.WaitForStatusAsync(accepted.RunId, RunStatus.Completed);
        Assert.Equal("long", run.CurrentStepId);
        Assert.Equal("Agent Framework workflows in YAML", run.Input!["text"]!.GetValue<string>());
        Assert.Equal(6, run.StepOutputs["count"]!["words"]!.GetValue<int>());
        Assert.NotNull(run.FinalOutput);
    }

    [Fact]
    public async Task SubmitRun_InvalidInput_Returns400WithMessageAndErrors()
    {
        await using var host = await EngineTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/engine/workflows/hello/runs", new { text = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Text is required.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("/text", problem.RootElement.GetProperty("errors")[0].GetProperty("pointer").GetString());
    }

    [Fact]
    public async Task SubmitRun_UnknownWorkflow_Returns404()
    {
        await using var host = await EngineTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/engine/workflows/ghost/runs", new { text = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitRun_UnavailableModule_Returns503()
    {
        await using var host = await EngineTestHost.StartAsync();
        host.Modules.DownModules.Add("demo");
        await host.Services.GetService<Flowgent.Runtime.Modules.ModuleCatalogService>()!.RefreshAllAsync();

        var response = await host.Client.PostAsJsonAsync("/api/engine/workflows/hello/runs", new { text = "x" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Runs_FilterAndReturn404ForUnknownRuns()
    {
        await using var host = await EngineTestHost.StartAsync();
        var ok = await host.SubmitAsync(new { text = "Agent Framework workflows in YAML" });
        var ko = await host.SubmitAsync(new { text = "short" });
        await host.WaitForStatusAsync(ok.RunId, RunStatus.Completed);
        await host.WaitForStatusAsync(ko.RunId, RunStatus.Failed);

        var failed = await host.Client.GetFromJsonAsync<List<RunSummary>>("/api/engine/runs?status=Failed", Json);
        var all = await host.Client.GetFromJsonAsync<List<RunSummary>>("/api/engine/runs?moduleId=demo&workflowId=hello", Json);

        Assert.Equal([ko.RunId], failed!.Select(r => r.Id));
        Assert.Equal(2, all!.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/engine/runs/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task HumanInputs_ListApproveRejectWithStatusCodes()
    {
        await using var host = await EngineTestHost.StartAsync(TestModules.AddReviewWorkflow);
        var first = await host.SubmitAsync(new { subject = "alpha" }, "review");
        var second = await host.SubmitAsync(new { subject = "beta" }, "review");
        await host.WaitForStatusAsync(first.RunId, RunStatus.AwaitingInput);
        await host.WaitForStatusAsync(second.RunId, RunStatus.AwaitingInput);
        await WaitForPendingAsync(host, 2);

        var pending = await host.Client.GetFromJsonAsync<List<HumanInputInfo>>("/api/engine/human-inputs?workflowId=review", Json);
        Assert.Equal(2, pending!.Count);
        Assert.Equal("Review alpha", pending.Single(p => p.RunId == first.RunId).Review.Title);

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsync($"/api/engine/human-inputs/{first.RunId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsJsonAsync($"/api/engine/human-inputs/{second.RunId}/reject", new RejectRequest("No"))).StatusCode);

        Assert.Equal(RunStatus.Completed, (await host.WaitForRunAsync(first.RunId, r => r.Status.IsTerminal())).Status);
        var rejected = await host.WaitForRunAsync(second.RunId, r => r.Status.IsTerminal());
        Assert.Equal("declined", rejected.CurrentStepId);
        Assert.Equal("No", rejected.Detail);

        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsync($"/api/engine/human-inputs/{first.RunId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsync($"/api/engine/human-inputs/{Guid.NewGuid()}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Reject_WithoutBody_IsAccepted()
    {
        await using var host = await EngineTestHost.StartAsync(TestModules.AddReviewWorkflow);
        var run = await host.SubmitAsync(new { subject = "alpha" }, "review");
        await WaitForPendingAsync(host, 1);

        var response = await host.Client.PostAsync($"/api/engine/human-inputs/{run.RunId}/reject", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Rejected", (await host.WaitForRunAsync(run.RunId, r => r.Status.IsTerminal())).Detail);
    }

    [Fact]
    public async Task Abort_RunningRun_Returns202AndFailsTheRun()
    {
        await using var host = await EngineTestHost.StartAsync(TestModules.AddReviewWorkflow);
        var run = await host.SubmitAsync(new { subject = "alpha" }, "review");
        await WaitForPendingAsync(host, 1);

        var response = await host.Client.PostAsync($"/api/engine/runs/{run.RunId}/abort", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var aborted = await host.WaitForRunAsync(run.RunId, r => r.Status.IsTerminal());
        Assert.Equal(RunStatus.Failed, aborted.Status);
        Assert.Equal("Aborted by user", aborted.Detail);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsync($"/api/engine/runs/{run.RunId}/abort", null)).StatusCode);
    }

    [Fact]
    public async Task Abort_QueuedRun_Returns204()
    {
        await using var host = await EngineTestHost.StartAsync(configuration: new Dictionary<string, string?> { ["Flowgent:InboxPollingIntervalSeconds"] = "3600" });
        // The first polling cycle runs at startup; wait so the next message stays queued.
        await Task.Delay(200);
        var run = await host.SubmitAsync(new { text = "Agent Framework workflows in YAML" });

        var response = await host.Client.PostAsync($"/api/engine/runs/{run.RunId}/abort", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var aborted = await host.GetRunAsync(run.RunId);
        Assert.Equal(RunStatus.Failed, aborted.Status);
        Assert.Equal("Aborted by user", aborted.Detail);
    }

    [Fact]
    public async Task Abort_UnknownRun_Returns404()
    {
        await using var host = await EngineTestHost.StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsync($"/api/engine/runs/{Guid.NewGuid()}/abort", null)).StatusCode);
    }

    [Fact]
    public async Task Events_AreNewestFirstAndCappedAt200()
    {
        await using var host = await EngineTestHost.StartAsync();
        var run = await host.SubmitAsync(new { text = "Agent Framework workflows in YAML" });
        await host.WaitForStatusAsync(run.RunId, RunStatus.Completed);

        var events = await host.Client.GetFromJsonAsync<List<RunEventInfo>>($"/api/engine/events?runId={run.RunId}", Json);
        var capped = await host.Client.GetFromJsonAsync<List<RunEventInfo>>("/api/engine/events?limit=5000", Json);
        var limited = await host.Client.GetFromJsonAsync<List<RunEventInfo>>("/api/engine/events?limit=2", Json);

        Assert.Equal(RunEventKind.Completed, events![0].Kind);
        Assert.Equal(RunEventKind.Queued, events[^1].Kind);
        Assert.True(capped!.Count <= 200);
        Assert.Equal(2, limited!.Count);
    }

    [Fact]
    public async Task Agents_ShowWorkingWhileAssignedToANonTerminalRun()
    {
        await using var host = await EngineTestHost.StartAsync(TestModules.AddReviewWorkflow);
        var idle = Assert.Single((await host.Client.GetFromJsonAsync<List<AgentInfo>>("/api/engine/agents", Json))!);
        Assert.Equal("Idle", idle.Status);
        Assert.Equal("Writers", idle.Group);

        var run = await host.SubmitAsync(new { subject = "alpha" }, "review");
        await WaitForPendingAsync(host, 1);
        var working = Assert.Single((await host.Client.GetFromJsonAsync<List<AgentInfo>>("/api/engine/agents?moduleId=demo", Json))!);

        Assert.Equal("Working", working.Status);
        Assert.Equal(run.RunId, working.CurrentRunId);
        Assert.Equal("alpha", working.CurrentRunTitle);
        Assert.Equal("approval", working.CurrentStepId);
    }

    [Fact]
    public async Task Agents_Return404WhenVisualizationIsDisabled()
    {
        await using var host = await EngineTestHost.StartAsync(configuration: new Dictionary<string, string?> { ["Flowgent:EnableVisualization"] = "false" });

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/engine/agents")).StatusCode);
    }

    [Fact]
    public async Task Telemetry_IsAttributedToAgents()
    {
        await using var host = await EngineTestHost.StartAsync();
        var run = await host.SubmitAsync(new { text = "Agent Framework workflows in YAML" });
        await host.WaitForStatusAsync(run.RunId, RunStatus.Completed);

        var agent = await host.Client.GetFromJsonAsync<AgentTelemetry>("/api/engine/agents/demo_writer/telemetry", Json);
        var summary = await host.Client.GetFromJsonAsync<TelemetrySummary>("/api/engine/telemetry/summary?moduleId=demo", Json);

        Assert.Equal(1, agent!.TotalCalls);
        Assert.Equal("summarize", agent.LastCall!.StepId);
        Assert.Equal(1, summary!.TotalCalls);
        Assert.Equal(0, summary.ErrorRate);
    }

    [Fact]
    public async Task Inbox_ListsMessagesWithStatusFilter()
    {
        await using var host = await EngineTestHost.StartAsync();
        var run = await host.SubmitAsync(new { text = "Agent Framework workflows in YAML" });
        await host.WaitForStatusAsync(run.RunId, RunStatus.Completed);
        await WaitForInboxAsync(host, "processed", 1);

        var processed = await host.Client.GetFromJsonAsync<List<InboxMessageInfo>>("/api/engine/inbox?status=processed", Json);
        var queued = await host.Client.GetFromJsonAsync<List<InboxMessageInfo>>("/api/engine/inbox?status=queued", Json);

        var message = Assert.Single(processed!);
        Assert.Equal(run.MessageId, message.Id);
        Assert.Equal("hello", message.WorkflowId);
        Assert.Empty(queued!);
        Assert.Equal(run.MessageId, (await host.Client.GetFromJsonAsync<InboxMessageInfo>($"/api/engine/inbox/{run.MessageId}", Json))!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/engine/inbox/{Guid.NewGuid()}")).StatusCode);
    }

    // The custom 'workflow' schema is gone with the custom format (documented behavior change): /api/engine/schemas
    // now serves only 'module' and 'samples'.
    [Theory]
    [InlineData("module")]
    [InlineData("samples")]
    public async Task Schemas_ArePublished(string name)
    {
        await using var host = await EngineTestHost.StartAsync();

        var schema = JsonNode.Parse(await host.Client.GetStringAsync($"/api/engine/schemas/{name}"))!;

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/engine/schemas/ghost")).StatusCode);
    }

    [Fact]
    public async Task Health_IsServed()
    {
        await using var host = await EngineTestHost.StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health")).StatusCode);
    }

    private static async Task WaitForPendingAsync(EngineTestHost host, int count)
    {
        for (var i = 0; i < 3000; i++)
        {
            var pending = await host.Client.GetFromJsonAsync<List<HumanInputInfo>>("/api/engine/human-inputs", Json);
            if (pending!.Count >= count)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Human inputs did not appear.");
    }

    private static async Task WaitForInboxAsync(EngineTestHost host, string status, int count)
    {
        for (var i = 0; i < 3000; i++)
        {
            if ((await host.Client.GetFromJsonAsync<List<InboxMessageInfo>>($"/api/engine/inbox?status={status}", Json))!.Count >= count)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Inbox messages did not reach the expected status.");
    }
}
