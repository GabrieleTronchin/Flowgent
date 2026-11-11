namespace Flowgent.Engine.Tests.Runtime;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Tests.Helpers;
using Flowgent.Runtime.HumanInput;

public class HumanInputAndStepKindTests
{
    private static Task<TestEngine> StartReviewEngineAsync() => TestEngine.StartAsync(TestModules.AddReviewWorkflow);

    private static JsonObject Subject(string subject) => new() { ["subject"] = subject };

    [Fact]
    public async Task HumanInput_PausesRunWithReviewPayload()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("alpha docs"), "review");

        var execution = engine.Orchestrator.ExecuteAsync(runId);
        await engine.WaitForHumanInputAsync(runId);
        var run = await engine.WaitForStatusAsync(runId, RunStatus.AwaitingInput);

        Assert.Equal("approval", run.CurrentStepId);
        Assert.Equal("demo_writer", run.AssignedAgentId);
        // Observability change (documented): the AwaitingInput detail is the Question prompt payload (the review JSON),
        // not a fixed "Waiting for review" label; the structured review is surfaced via the pending human-input below.
        var pending = Assert.Single(engine.HumanInputs.GetPending());
        Assert.Equal("Review alpha docs", pending.Review.Title);
        Assert.Equal([("Team", "Alpha"), ("Role", "Writer")], pending.Review.Fields.Select(f => (f.Label, f.Value!)));
        Assert.Equal("alpha docs", pending.RunTitle);

        await engine.HumanInput.ApproveAsync(runId);
        await execution;
    }

    [Fact]
    public async Task Approve_ContinuesAlongTheApprovedTransition()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("alpha urgent"), "review");
        var execution = engine.Orchestrator.ExecuteAsync(runId);
        await engine.WaitForHumanInputAsync(runId);

        var (result, _) = await engine.HumanInput.ApproveAsync(runId);
        await execution;

        Assert.Equal(HumanInputResult.Completed, result);
        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("done", run.CurrentStepId);
        Assert.Equal("URGENT Alpha", run.Detail);
        Assert.Equal("approved", run.StepOutputs["approval"]!["decision"]!.GetValue<string>());
        Assert.Empty(engine.HumanInputs.GetPending());
    }

    [Fact]
    public async Task Reject_FollowsTheDefaultTransitionWithReason()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("beta"), "review");
        var execution = engine.Orchestrator.ExecuteAsync(runId);
        await engine.WaitForHumanInputAsync(runId);

        await engine.HumanInput.RejectAsync(runId, "Needs more detail");
        await execution;

        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("declined", run.CurrentStepId);
        Assert.Equal("Needs more detail", run.Detail);
        var events = await engine.EventsAsync(runId);
        Assert.Contains(events, e => e.Kind == RunEventKind.InputReceived && e.Detail == "rejected: Needs more detail");
    }

    [Fact]
    public async Task RejectWithoutReason_UsesTheTemplateFallback()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("beta"), "review");
        var execution = engine.Orchestrator.ExecuteAsync(runId);
        await engine.WaitForHumanInputAsync(runId);

        await engine.HumanInput.RejectAsync(runId, "  ");
        await execution;

        Assert.Equal("Rejected", (await engine.Store.GetAsync(runId))!.Detail);
    }

    [Fact]
    public async Task Approve_UnknownRun_IsNotFound()
    {
        await using var engine = await StartReviewEngineAsync();

        var (result, error) = await engine.HumanInput.ApproveAsync(Guid.NewGuid());

        Assert.Equal(HumanInputResult.NotFound, result);
        Assert.Contains("not found", error);
    }

    [Fact]
    public async Task Approve_CompletedRun_IsConflict()
    {
        await using var engine = await StartReviewEngineAsync();
        var run = await engine.RunAsync(Subject("alpha beta"), "review");

        var (result, error) = await engine.HumanInput.ApproveAsync(run.Id);

        Assert.Equal(HumanInputResult.Conflict, result);
        Assert.Contains("already completed", error);
    }

    [Fact]
    public async Task Approve_QueuedRun_IsConflict()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("alpha"), "review");

        var (result, error) = await engine.HumanInput.ApproveAsync(runId);

        Assert.Equal(HumanInputResult.Conflict, result);
        Assert.Contains("not awaiting input", error);
    }

    [Fact]
    public async Task Abort_ReleasesPendingInputAndRecordsSingleFailedTransition()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("alpha"), "review");
        var execution = engine.Orchestrator.ExecuteAsync(runId);
        await engine.WaitForHumanInputAsync(runId);

        Assert.True(engine.Registry.TryAbort(runId));
        await execution;

        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("Aborted by user", run.Detail);
        Assert.Equal("approval", run.CurrentStepId);
        var events = await engine.EventsAsync(runId);
        Assert.Single(events, e => e.Kind == RunEventKind.Failed);
        Assert.DoesNotContain(events, e => e.Kind == RunEventKind.InputReceived);
        Assert.Empty(engine.HumanInputs.GetPending());
        Assert.False(engine.Registry.IsRunning(runId));
    }

    [Fact]
    public async Task HostShutdown_CancelsTheRun()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("alpha"), "review");
        using var host = new CancellationTokenSource();
        var execution = engine.Orchestrator.ExecuteAsync(runId, host.Token);
        await engine.WaitForHumanInputAsync(runId);

        await host.CancelAsync();
        var result = await execution;

        Assert.Equal(RunStatus.Cancelled, result.Status);
        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.Equal("Workflow was cancelled.", run.Detail);
    }

    // Observability change (same Gap-3 class as support RoutingFailures; FEAT-004-observability-gap-analysis.md Gap 3):
    // under MAF a routing failure is a GotoAction to an explicit failure terminal, so CurrentStepId is that terminal
    // (fail_ambiguous / fail_no_team), not the rule step "route". The verbatim failure message is preserved.
    [Theory]
    [InlineData("alpha and beta", "fail_ambiguous", "Ambiguous: both alpha and beta.")]
    [InlineData("gamma", "fail_no_team", "No team for 'gamma'.")]
    public async Task RuleStep_FailsWithDeclaredMessages(string subject, string terminal, string expected)
    {
        await using var engine = await StartReviewEngineAsync();

        var run = await engine.RunAsync(Subject(subject), "review");

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(terminal, run.CurrentStepId);
        Assert.Equal(expected, run.Detail);
    }

    [Theory]
    [InlineData("ALPHA", "Alpha")]
    [InlineData("about Beta", "Beta")]
    public async Task RuleStep_MatchesCaseInsensitivelyByDefault(string subject, string team)
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject(subject), "review");
        var execution = engine.Orchestrator.ExecuteAsync(runId);
        await engine.WaitForHumanInputAsync(runId);

        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal(team, run.StepOutputs["route"]!["team"]!.GetValue<string>());
        engine.Registry.TryAbort(runId);
        await execution;
    }

    // The declarative route step computes its team branch in PowerFx (the custom rule-step policies onMultipleMatches:
    // first and onNoMatch: { output } no longer exist — documented behavior change): both-match -> fail_ambiguous,
    // no-match -> fail_no_team, else the matching team. The map-style assign step is now a PowerFx If, so there is no
    // "missing lookup" failure to reproduce. Behavior parity is covered by the routing/assign assertions below.
    [Fact]
    public async Task AssignStep_ComputesRoleAndLabelFromTheRoutedTeam()
    {
        await using var engine = await StartReviewEngineAsync();
        var runId = await engine.SubmitAsync(Subject("alpha docs"), "review");
        var execution = engine.Orchestrator.ExecuteAsync(runId);
        await engine.WaitForHumanInputAsync(runId);

        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal("Writer", run.StepOutputs["assign"]!["role"]!.GetValue<string>());
        Assert.Equal("Alpha-Writer", run.StepOutputs["assign"]!["label"]!.GetValue<string>());
        engine.Registry.TryAbort(runId);
        await execution;
    }

    [Fact]
    public async Task ConcurrentRuns_AreIsolated()
    {
        await using var engine = await StartReviewEngineAsync();
        // Isolation is proven with a handful of concurrent runs; each MAF run carries per-run checkpoint I/O, so 20 at
        // once in-process only adds machine load without strengthening the assertion.
        var subjects = Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"alpha {i}" : $"beta urgent {i}").ToList();

        var runIds = new List<Guid>();
        foreach (var subject in subjects)
        {
            runIds.Add(await engine.SubmitAsync(Subject(subject), "review"));
        }

        var executions = runIds.Select(id => engine.Orchestrator.ExecuteAsync(id)).ToList();
        foreach (var id in runIds)
        {
            await engine.WaitForHumanInputAsync(id);
        }

        Assert.Equal(6, engine.HumanInputs.GetPending().Count);
        foreach (var id in runIds)
        {
            await engine.HumanInput.ApproveAsync(id);
        }

        await Task.WhenAll(executions);

        for (var i = 0; i < subjects.Count; i++)
        {
            var run = (await engine.Store.GetAsync(runIds[i]))!;
            Assert.Equal(RunStatus.Completed, run.Status);
            Assert.Equal(subjects[i], run.Title);
            Assert.Equal(i % 2 == 0 ? "normal Alpha" : "URGENT Beta", run.Detail);
        }
    }

    // Removed as documented behavior changes under the MAF declarative model: there is no per-step demo: block (Demo
    // responses live in DemoResponseProvider, keyed by (workflow, stepId); an unmapped step throws "No demo response is
    // defined", covered by the Demo smoke/parity suites), no custom maxSteps loop guard, and no Scriban assignAgent
    // template (the assigned agent id is a Global.assignedAgentId PowerFx value resolved by MafAgentProvider).
}
