namespace Flowgent.Engine.Tests.Runtime;

using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Tests.Helpers;
using Flowgent.Runtime.Runs;
using Microsoft.Extensions.DependencyInjection;

public class PersistenceTests
{
    private static readonly (RunEventKind Kind, RunStatus Status)[] Transitions =
    [
        (RunEventKind.StepStarted, RunStatus.Running),
        (RunEventKind.StepCompleted, RunStatus.Running),
        (RunEventKind.AwaitingInput, RunStatus.AwaitingInput),
        (RunEventKind.InputReceived, RunStatus.Running),
        (RunEventKind.Failed, RunStatus.Failed),
        (RunEventKind.Completed, RunStatus.Completed),
    ];

    // Dual-write: every transition updates the run and appends exactly one event with the same step/status/detail.
    [Property(MaxTest = 30)]
    public Property EveryTransition_UpdatesRunAndAppendsOneEvent(int[] choices, NonNull<string> detail)
    {
        return Prop.ToProperty(CheckDualWriteAsync(choices.Take(10).ToArray(), detail.Get).GetAwaiter().GetResult());
    }

    private static async Task<bool> CheckDualWriteAsync(int[] choices, string detail)
    {
        await using var engine = await TestEngine.StartAsync();
        var runId = await engine.SubmitAsync(new JsonObject { ["text"] = "x" });

        for (var i = 0; i < choices.Length; i++)
        {
            var (kind, status) = Transitions[Math.Abs(choices[i] % Transitions.Length)];
            var stepId = $"step_{i}";
            var run = await engine.Store.RecordAsync(new RunTransition(runId, kind, status, stepId, detail));
            var events = await engine.EventsAsync(runId);
            var last = events[^1];

            if (events.Count != i + 2 || run.Status != status || run.CurrentStepId != stepId || run.Detail != detail
                || last.Kind != kind || last.Status != status || last.StepId != stepId || last.Detail != detail
                || last.PreviousStepId != (i == 0 ? null : $"step_{i - 1}"))
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public async Task Transitions_ArePublishedToSubscribers()
    {
        await using var engine = await TestEngine.StartAsync();
        using var subscription = engine.Services.GetRequiredService<RunUpdateChannel>().Subscribe();

        var run = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });

        var updates = new List<RunUpdate>();
        while (subscription.Reader.TryRead(out var update))
        {
            updates.Add(update);
        }

        Assert.Equal(
            [RunEventKind.Queued, RunEventKind.StepStarted, RunEventKind.StepCompleted, RunEventKind.StepStarted, RunEventKind.StepCompleted, RunEventKind.Completed],
            updates.Select(u => u.Kind));
        Assert.All(updates, u => Assert.Equal(run.Id, u.RunId));
        Assert.All(updates, u => Assert.Equal("hello", u.WorkflowId));
        Assert.Equal("long", updates[^1].StepId);
        Assert.Equal(RunStatus.Completed, updates[^1].Status);
    }

    [Fact]
    public async Task Subscribers_AreIndependent()
    {
        var channel = new RunUpdateChannel();
        using var a = channel.Subscribe();
        var b = channel.Subscribe();
        b.Dispose();

        channel.Publish(new RunUpdate(Guid.NewGuid(), "m", "w", "s", RunStatus.Running, RunEventKind.StepStarted, DateTimeOffset.UtcNow, null, null));

        Assert.True(a.Reader.TryRead(out _));
        Assert.False(b.Reader.TryRead(out _));
        Assert.Equal(1, channel.SubscriberCount);
    }

    [Fact]
    public async Task Queries_FilterByModuleWorkflowStatusAndRun()
    {
        await using var engine = await TestEngine.StartAsync(TestModules.AddReviewWorkflow);
        var hello = await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });
        var review = await engine.RunAsync(new JsonObject { ["subject"] = "gamma" }, "review");

        Assert.Equal([hello.Id], (await engine.Store.ListAsync(new RunQuery(WorkflowId: "hello"))).Select(r => r.Id));
        Assert.Equal([review.Id], (await engine.Store.ListAsync(new RunQuery(Status: RunStatus.Failed))).Select(r => r.Id));
        Assert.Equal(2, (await engine.Store.ListAsync(new RunQuery(ModuleId: "demo"))).Count);
        Assert.Empty(await engine.Store.ListAsync(new RunQuery(ModuleId: "other")));
        Assert.All(await engine.Store.ListEventsAsync(new EventQuery(RunId: review.Id)), e => Assert.Equal(review.Id, e.RunId));
        Assert.All(await engine.Store.ListEventsAsync(new EventQuery(WorkflowId: "hello")), e => Assert.Equal("hello", e.WorkflowId));
        Assert.Equal(3, (await engine.Store.ListEventsAsync(new EventQuery(Limit: 3))).Count);
    }

    [Fact]
    public async Task Events_AreReturnedNewestFirst()
    {
        await using var engine = await TestEngine.StartAsync();
        await engine.RunAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });

        var events = await engine.Store.ListEventsAsync(new EventQuery());

        Assert.Equal(RunEventKind.Completed, events[0].Kind);
        Assert.Equal(RunEventKind.Queued, events[^1].Kind);
    }
}
