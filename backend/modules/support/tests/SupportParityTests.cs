namespace Flowgent.Modules.Support.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Testing;

/// <summary>
/// Every dashboard template (support sample) submitted in Demo mode through the gateway reaches the same final
/// step and outcome as the original hard-coded pipeline.
/// </summary>
public sealed class SupportParityTests : IAsyncLifetime
{
    private PlatformHarness _platform = null!;

    public static TheoryData<string, string, string, string, string, string> CodeScenarios => new()
    {
        // sample, category, team, agent, component, severity
        { "scenario-a1", "BackendBug", "TeamA", "TeamA_BackendDeveloper", "OrderController.cs", "High" },
        { "scenario-a2", "FrontendBug", "TeamA", "TeamA_FrontendDeveloper", "OrderSummary.razor", "Medium" },
        { "scenario-a3", "QualityTestIssue", "TeamA", "TeamA_QAEngineer", "OrderServiceTests.cs", "Medium" },
        { "scenario-b1", "BackendBug", "TeamB", "TeamB_BackendDeveloper", "UserController.cs", "Critical" },
        { "scenario-b2", "FrontendBug", "TeamB", "TeamB_FrontendDeveloper", "UserProfile.razor", "Medium" },
        { "scenario-b3", "QualityTestIssue", "TeamB", "TeamB_QAEngineer", "UserServiceTests.cs", "Medium" },
    };

    public async ValueTask InitializeAsync() => _platform = await SupportPlatform.StartAsync();

    public async ValueTask DisposeAsync() => await _platform.DisposeAsync();

    [Theory]
    [MemberData(nameof(CodeScenarios))]
    public async Task CodeScenario_Approved_GeneratesTheCodeChange(string sampleId, string category, string team, string agent, string component, string severity)
    {
        var runId = await SubmitSampleAsync(sampleId);

        var input = await _platform.WaitForHumanInputAsync(runId);
        var awaiting = await _platform.GetRunAsync(runId);
        Assert.Equal(RunStatus.AwaitingInput, awaiting.Status);
        Assert.Equal("approval", awaiting.CurrentStepId);
        Assert.Equal(agent, awaiting.AssignedAgentId);
        Assert.Equal($"Proposed fix: {component}", input.Review.Title);
        Assert.Equal(severity, input.Review.Fields.Single(f => f.Label == "Severity").Value);

        Assert.Equal(HttpStatusCode.NoContent, (await _platform.ApproveAsync(runId)).StatusCode);
        var run = await _platform.WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("code_change_generated", run.CurrentStepId);
        Assert.Equal($"[Demo] Fix {component}", run.Detail);
        Assert.Equal(category, run.StepOutputs["classify"]!["category"]!.GetValue<string>());
        Assert.Equal(team, run.StepOutputs["route_team"]!["teamName"]!.GetValue<string>());
        Assert.Equal(agent, run.StepOutputs["assign_agent"]!["agentId"]!.GetValue<string>());
        Assert.Equal(agent, run.AssignedAgentId);
        Assert.Equal(component, run.StepOutputs["locate_component"]!["component"]!.GetValue<string>());
        Assert.Equal($"[Demo] Fix {component}", run.FinalOutput!["title"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(CodeScenarios))]
    public async Task CodeScenario_Rejected_EndsInManualReview(string sampleId, string category, string team, string agent, string component, string severity)
    {
        _ = (category, team, agent, component, severity);
        var runId = await SubmitSampleAsync(sampleId);
        await _platform.WaitForHumanInputAsync(runId);

        Assert.Equal(HttpStatusCode.NoContent, (await _platform.RejectAsync(runId, "Fix is too risky")).StatusCode);
        var run = await _platform.WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("manual_review_required", run.CurrentStepId);
        Assert.Equal("Fix is too risky", run.Detail);
        Assert.Null(run.StepOutputs["generate_code"]);
    }

    [Fact]
    public async Task Rejected_WithoutReason_UsesRejected()
    {
        var runId = await SubmitSampleAsync("scenario-a1");
        await _platform.WaitForHumanInputAsync(runId);

        await _platform.RejectAsync(runId, null);

        Assert.Equal("Rejected", (await _platform.WaitForTerminalAsync(runId)).Detail);
    }

    [Fact]
    public async Task Aborted_WhileAwaitingApproval_FailsWithAbortedByUser()
    {
        var runId = await SubmitSampleAsync("scenario-b1");
        await _platform.WaitForHumanInputAsync(runId);

        Assert.Equal(HttpStatusCode.Accepted, (await _platform.AbortAsync(runId)).StatusCode);
        var run = await _platform.WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("Aborted by user", run.Detail);
        Assert.Equal("approval", run.CurrentStepId);
        var events = await _platform.GetEventsAsync(runId);
        Assert.Single(events, e => e.Kind == RunEventKind.Failed);
        Assert.Equal(HttpStatusCode.Conflict, (await _platform.AbortAsync(runId)).StatusCode);
    }

    [Fact]
    public async Task OutOfScope_EndsAtClassification()
    {
        var run = await _platform.WaitForTerminalAsync(await SubmitSampleAsync("edge-out-of-scope"));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("classified_out_of_scope", run.CurrentStepId);
        Assert.Equal("[Demo] No code-related signal found; looks like a general support request.", run.Detail);
        Assert.False(run.StepOutputs["classify"]!["isCodeRelated"]!.GetValue<bool>());
    }

    // BEHAVIOUR CHANGE (FEAT-004, MAF declarative — see .agents/.../FEAT-004-observability-gap-analysis.md Gap 3):
    // a routing failure is now modelled as a GotoAction to an explicit failure terminal (fail_ambiguous / fail_no_app),
    // so the failing step id is that terminal, not "route_team". The outcome (Failed), the verbatim reason and the
    // classify output are preserved. The old engine had the rule step fail in place; under MAF the failing step is the
    // terminal, which is the faithful declarative model (the dashboard treats CurrentStepId generically).
    [Theory]
    [InlineData("edge-ambiguous-routing", "fail_ambiguous", "Ambiguous routing: both ApplicationA and ApplicationB are mentioned.")]
    [InlineData("edge-failed-routing", "fail_no_app", "Cannot determine affected application: neither ApplicationA nor ApplicationB is mentioned.")]
    public async Task RoutingFailures_FailAtTeamRouting(string sampleId, string terminal, string reason)
    {
        var run = await _platform.WaitForTerminalAsync(await SubmitSampleAsync(sampleId));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(terminal, run.CurrentStepId);
        Assert.Equal(reason, run.Detail);
        Assert.Equal("BackendBug", run.StepOutputs["classify"]!["category"]!.GetValue<string>());
    }

    [Fact]
    public async Task EmptyInput_IsRejectedWith400()
    {
        var sample = SupportPlatform.Sample("edge-empty-input");

        var response = await _platform.Client.PostAsJsonAsync("/api/support/emails", sample.Input);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Subject and Body are required.", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task EmptyInput_IsAlsoRejectedByTheEngineIntake()
    {
        var sample = SupportPlatform.Sample("edge-empty-input");

        var response = await _platform.Client.PostAsJsonAsync("/api/engine/workflows/support-email/runs", sample.Input);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Subject and Body are required.", problem.RootElement.GetProperty("detail").GetString());
    }

    // BEHAVIOUR CHANGE (FEAT-004, MAF declarative — see .agents/.../FEAT-004-observability-gap-analysis.md Gap 2):
    // the seam surfaces every logical step's output uniformly from run state, so EVERY step now emits both StepStarted
    // and StepCompleted in deterministic workflow order (the old engine suppressed StepStarted for synchronous rule/map
    // steps — an incidental artifact of its executors, not a dashboard contract). The HITL gate surfaces its
    // AwaitingInput/InputReceived before the approval step's own StepStarted/StepCompleted. The per-step Detail values,
    // the pipeline order and the terminal are preserved.
    [Fact]
    public async Task EventLog_FollowsThePipelineSteps()
    {
        var runId = await SubmitSampleAsync("scenario-a1");
        await _platform.WaitForHumanInputAsync(runId);
        await _platform.ApproveAsync(runId);
        await _platform.WaitForTerminalAsync(runId);

        var events = await _platform.GetEventsAsync(runId);

        Assert.Equal(
            [
                "Queued:", "StepStarted:classify", "StepCompleted:classify", "StepStarted:route_team", "StepCompleted:route_team",
                "StepStarted:assign_agent", "StepCompleted:assign_agent", "StepStarted:locate_component", "StepCompleted:locate_component",
                "StepStarted:resolve", "StepCompleted:resolve", "AwaitingInput:approval", "InputReceived:approval",
                "StepStarted:approval", "StepCompleted:approval", "StepStarted:generate_code", "StepCompleted:generate_code",
                "Completed:code_change_generated",
            ],
            events.Select(e => $"{e.Kind}:{e.StepId}"));
        Assert.Equal("BackendBug (92%)", events.Single(e => e is { Kind: RunEventKind.StepCompleted, StepId: "classify" }).Detail);
        Assert.Equal("TeamA", events.Single(e => e is { Kind: RunEventKind.StepCompleted, StepId: "route_team" }).Detail);
        Assert.Equal("TeamA_BackendDeveloper", events.Single(e => e is { Kind: RunEventKind.StepCompleted, StepId: "assign_agent" }).Detail);
    }

    [Fact]
    public async Task ResolutionAndCodeGeneration_AreAttributedToTheAssignedAgent()
    {
        var runId = await SubmitSampleAsync("scenario-b2");
        await _platform.WaitForHumanInputAsync(runId);
        await _platform.ApproveAsync(runId);
        await _platform.WaitForTerminalAsync(runId);

        var telemetry = await _platform.Client.GetFromJsonAsync<AgentTelemetry>("/api/engine/agents/TeamB_FrontendDeveloper/telemetry", PlatformHarness.Json);
        var summary = await _platform.Client.GetFromJsonAsync<TelemetrySummary>("/api/engine/telemetry/summary", PlatformHarness.Json);

        Assert.Equal(2, telemetry!.TotalCalls);
        Assert.Equal("generate_code", telemetry.LastCall!.StepId);
        Assert.Equal(3, summary!.TotalCalls);
    }

    [Fact]
    public async Task SupportEmail_ReturnsAcceptedWithMessageAndRunIds()
    {
        var response = await _platform.Client.PostAsJsonAsync("/api/support/emails", SupportPlatform.Sample("edge-out-of-scope").Input);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var runId = Guid.Parse(body["runId"]!.GetValue<string>());
        Assert.NotEqual(Guid.Empty, Guid.Parse(body["messageId"]!.GetValue<string>()));
        Assert.Equal($"/api/engine/runs/{runId}", response.Headers.Location!.OriginalString);
        Assert.Equal("How do I reset my password?", (await _platform.GetRunAsync(runId)).Title);
    }

    [Fact]
    public async Task SupportEmail_AcceptsPascalCaseProperties()
    {
        var response = await _platform.Client.PostAsJsonAsync(
            "/api/support/emails",
            new Dictionary<string, string> { ["Sender"] = "a@b.c", ["Subject"] = "Crash in Application A", ["Body"] = "NullReferenceException in OrderController.cs" },
            new JsonSerializerOptions());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task ModuleRoute_AlsoAcceptsEmails()
    {
        var response = await _platform.Client.PostAsJsonAsync("/api/modules/support/emails", SupportPlatform.Sample("edge-out-of-scope").Input);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Agents_ListsTheSixSupportAgents()
    {
        var agents = await _platform.Client.GetFromJsonAsync<List<AgentInfo>>("/api/engine/agents?moduleId=support", PlatformHarness.Json);

        Assert.Equal(6, agents!.Count);
        Assert.All(agents, a => Assert.Equal("Idle", a.Status));
        Assert.Equal(["TeamA", "TeamB"], agents.Select(a => a.Group!).Distinct().Order());
    }

    [Fact]
    public async Task Workflow_ExposesSamplesToTheDashboard()
    {
        var workflow = await _platform.Client.GetFromJsonAsync<WorkflowInfo>("/api/engine/workflows/support-email", PlatformHarness.Json);

        Assert.Equal(
            ["scenario-a1", "scenario-a2", "scenario-a3", "scenario-b1", "scenario-b2", "scenario-b3", "edge-out-of-scope", "edge-ambiguous-routing", "edge-failed-routing", "edge-empty-input"],
            workflow!.Samples.Select(s => s.Id));
        Assert.Equal(["Application A", "Application B", "Edge Cases"], workflow.Samples.Select(s => s.Group!).Distinct());
        Assert.Equal("classify", workflow.Graph.Start);
        // BEHAVIOUR CHANGE (FEAT-004, MAF declarative — see .agents/.../FEAT-004-observability-gap-analysis.md Gaps 2/3):
        // routing failures are now explicit terminals (fail_ambiguous / fail_no_app), so the graph shows the 7 pipeline
        // steps plus 6 terminals (was 10 = 7 steps + 3 terminals, with routing failures folded into the rule step).
        Assert.Equal(13, workflow.Graph.Nodes.Count);
    }

    private async Task<Guid> SubmitSampleAsync(string sampleId)
    {
        var response = await _platform.Client.PostAsJsonAsync("/api/support/emails", SupportPlatform.Sample(sampleId).Input);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Guid.Parse(JsonNode.Parse(await response.Content.ReadAsStringAsync())!["runId"]!.GetValue<string>());
    }
}
