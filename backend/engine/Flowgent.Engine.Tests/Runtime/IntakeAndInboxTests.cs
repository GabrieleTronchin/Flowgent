namespace Flowgent.Engine.Tests.Runtime;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Tests.Helpers;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Intake;
using Flowgent.Runtime.Runs;
using Microsoft.Extensions.DependencyInjection;

public class IntakeAndInboxTests
{
    [Fact]
    public async Task Submit_ValidInput_CreatesQueuedRunEventAndInboxMessage()
    {
        await using var engine = await TestEngine.StartAsync();

        var result = await engine.Intake.SubmitAsync("hello", new JsonObject { ["text"] = "Hello world" });

        var accepted = Assert.IsType<IntakeResult.Accepted>(result);
        var run = (await engine.Store.GetAsync(accepted.RunId))!;
        Assert.Equal(RunStatus.Queued, run.Status);
        Assert.Equal("demo", run.ModuleId);
        Assert.Equal("hello", run.WorkflowId);
        Assert.Equal(1, run.WorkflowVersion);
        Assert.Equal("Hello world", run.Title);
        Assert.Equal(accepted.MessageId, run.InboxMessageId);
        var queued = Assert.Single(await engine.EventsAsync(run.Id));
        Assert.Equal(RunEventKind.Queued, queued.Kind);

        var message = Assert.Single(await engine.Services.GetRequiredService<IInboxStore>().ListAsync(null, null));
        Assert.Equal(accepted.MessageId, message.Id);
        Assert.Equal(accepted.RunId, message.RunId);
        Assert.Equal("queued", message.Status);
        Assert.Equal("""{"text":"Hello world"}""", message.Payload);
    }

    [Fact]
    public async Task Submit_InvalidInput_ReturnsWorkflowErrorMessageAndFieldErrors()
    {
        await using var engine = await TestEngine.StartAsync();

        var result = await engine.Intake.SubmitAsync("hello", new JsonObject { ["text"] = "" });

        var invalid = Assert.IsType<IntakeResult.Invalid>(result);
        Assert.Equal("Text is required.", invalid.Message);
        Assert.Contains(invalid.Errors, e => e.Pointer == "/text");
        Assert.Empty(await engine.Store.ListAsync(new RunQuery()));
    }

    [Fact]
    public async Task Submit_NonObjectInput_IsInvalid()
    {
        await using var engine = await TestEngine.StartAsync();

        Assert.IsType<IntakeResult.Invalid>(await engine.Intake.SubmitAsync("hello", JsonValue.Create("text")));
        Assert.IsType<IntakeResult.Invalid>(await engine.Intake.SubmitAsync("hello", null));
    }

    [Fact]
    public async Task Submit_UnknownWorkflow_IsNotFound()
    {
        await using var engine = await TestEngine.StartAsync();

        Assert.IsType<IntakeResult.NotFound>(await engine.Intake.SubmitAsync("ghost", new JsonObject()));
    }

    [Fact]
    public async Task Submit_UnavailableModule_IsRejectedWithDescriptiveError()
    {
        await using var engine = await TestEngine.StartAsync();
        engine.Modules.DownModules.Add("demo");
        await engine.Services.GetRequiredService<Flowgent.Runtime.Modules.ModuleCatalogService>().RefreshAllAsync();

        var result = await engine.Intake.SubmitAsync("hello", new JsonObject { ["text"] = "x" });

        var unavailable = Assert.IsType<IntakeResult.Unavailable>(result);
        Assert.Contains("Module 'demo' is unavailable", unavailable.Message);
    }

    [Fact]
    public async Task InboxProcessor_RunsPendingMessagesAndMarksThemProcessed()
    {
        await using var engine = await TestEngine.StartAsync();
        var runId = await engine.SubmitAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });

        await engine.Inbox.ProcessPendingMessagesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Completed, (await engine.Store.GetAsync(runId))!.Status);
        var message = Assert.Single(await engine.Services.GetRequiredService<IInboxStore>().ListAsync("processed", null));
        Assert.Null(message.Error);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task InboxProcessor_WorkflowFailureIsNotAnInboxError()
    {
        await using var engine = await TestEngine.StartAsync();
        await engine.SubmitAsync(new JsonObject { ["text"] = "short" });

        await engine.Inbox.ProcessPendingMessagesAsync(TestContext.Current.CancellationToken);

        var message = Assert.Single(await engine.Services.GetRequiredService<IInboxStore>().ListAsync(null, null));
        Assert.Equal("processed", message.Status);
    }

    [Fact]
    public async Task InboxProcessor_UnavailableModule_MarksMessageFailedWithoutRetry()
    {
        await using var engine = await TestEngine.StartAsync();
        var runId = await engine.SubmitAsync(new JsonObject { ["text"] = "x" });
        engine.Modules.DownModules.Add("demo");
        await engine.Services.GetRequiredService<Flowgent.Runtime.Modules.ModuleCatalogService>().RefreshAllAsync();

        await engine.Inbox.ProcessPendingMessagesAsync(TestContext.Current.CancellationToken);
        await engine.Inbox.ProcessPendingMessagesAsync(TestContext.Current.CancellationToken);

        var message = Assert.Single(await engine.Services.GetRequiredService<IInboxStore>().ListAsync("failed", null));
        Assert.Equal("Module 'demo' is unavailable.", message.Error);
        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("Module 'demo' is unavailable.", run.Detail);
    }

    [Fact]
    public async Task InboxProcessor_AbortedQueuedRun_IsSkipped()
    {
        await using var engine = await TestEngine.StartAsync();
        var runId = await engine.SubmitAsync(new JsonObject { ["text"] = "x" });
        await engine.Store.RecordAsync(new RunTransition(runId, RunEventKind.Failed, RunStatus.Failed, null, RunRegistry.AbortReason));

        await engine.Inbox.ProcessPendingMessagesAsync(TestContext.Current.CancellationToken);

        var run = (await engine.Store.GetAsync(runId))!;
        Assert.Equal("Aborted by user", run.Detail);
        Assert.Equal(2, (await engine.EventsAsync(runId)).Count);
        Assert.Equal("processed", Assert.Single(await engine.Services.GetRequiredService<IInboxStore>().ListAsync(null, null)).Status);
    }

    [Fact]
    public async Task InboxProcessor_PausedRunDoesNotBlockLaterMessages()
    {
        await using var engine = await TestEngine.StartAsync(TestModules.AddReviewWorkflow);
        var paused = await engine.SubmitAsync(new JsonObject { ["subject"] = "alpha" }, "review");
        var later = await engine.SubmitAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });

        var started = await engine.Inbox.DispatchPendingMessagesAsync(TestContext.Current.CancellationToken);
        await engine.WaitForStatusAsync(later, RunStatus.Completed);

        Assert.Equal(RunStatus.AwaitingInput, (await engine.WaitForStatusAsync(paused, RunStatus.AwaitingInput)).Status);
        engine.Registry.TryAbort(paused);
        await Task.WhenAll(started);
    }

    [Fact]
    public async Task SequentialProcessing_StartsOneRunAtATime()
    {
        await using var engine = await TestEngine.StartAsync(
            TestModules.AddReviewWorkflow,
            configuration: new Dictionary<string, string?> { ["Flowgent:SequentialProcessing"] = "true" });
        var first = await engine.SubmitAsync(new JsonObject { ["subject"] = "alpha" }, "review");
        var second = await engine.SubmitAsync(new JsonObject { ["text"] = "Agent Framework workflows in YAML" });

        var cycle1 = await engine.Inbox.DispatchPendingMessagesAsync(TestContext.Current.CancellationToken);
        await engine.WaitForHumanInputAsync(first);
        var cycle2 = await engine.Inbox.DispatchPendingMessagesAsync(TestContext.Current.CancellationToken);

        _ = Assert.Single(cycle1);
        Assert.Empty(cycle2);
        Assert.Equal(RunStatus.Queued, (await engine.Store.GetAsync(second))!.Status);

        _ = await engine.HumanInput.ApproveAsync(first);
        await Task.WhenAll(cycle1);

        // Once the first run releases the sequential slot the second starts on the next poll. Dispatch until it leaves
        // the queue (the slot release and the manual dispatch can race under load), then wait for it to finish.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while ((await engine.Store.GetAsync(second))!.Status == RunStatus.Queued && DateTime.UtcNow < deadline)
        {
            await Task.WhenAll(await engine.Inbox.DispatchPendingMessagesAsync(TestContext.Current.CancellationToken));
            await Task.Delay(20);
        }

        Assert.Equal(RunStatus.Completed, (await engine.WaitForStatusAsync(second, RunStatus.Completed)).Status);
    }

    // Runs are started in the order the messages were received (FIFO), whatever their number.
    [Property(MaxTest = 25)]
    public Property InboxMessages_AreStartedInFifoOrder(PositiveInt count)
    {
        var n = Math.Min(count.Get, 15);
        return Prop.ToProperty(CheckFifoAsync(n).GetAwaiter().GetResult());
    }

    private static async Task<bool> CheckFifoAsync(int count)
    {
        var executor = new RecordingExecutor();
        await using var engine = await TestEngine.StartAsync(services: s => s.AddSingleton<IRunExecutor>(executor));
        var expected = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            expected.Add(await engine.SubmitAsync(new JsonObject { ["text"] = $"message {i}" }));
        }

        await engine.Inbox.ProcessPendingMessagesAsync();
        return executor.Started.SequenceEqual(expected);
    }

    // A failing executor never leaves a message pending (no infinite retries) and records the error.
    [Property(MaxTest = 25)]
    public Property FailedMessages_AreMarkedProcessedWithError(NonEmptyString error)
    {
        return Prop.ToProperty(CheckFailureAsync(error.Get).GetAwaiter().GetResult());
    }

    private static async Task<bool> CheckFailureAsync(string error)
    {
        var executor = new RecordingExecutor { Failure = error };
        await using var engine = await TestEngine.StartAsync(services: s => s.AddSingleton<IRunExecutor>(executor));
        await engine.SubmitAsync(new JsonObject { ["text"] = "x" });

        await engine.Inbox.ProcessPendingMessagesAsync();
        await engine.Inbox.ProcessPendingMessagesAsync();

        var messages = await engine.Services.GetRequiredService<IInboxStore>().ListAsync(null, null);
        return executor.Started.Count == 1 && messages.Single().Error == error && messages.Single().ProcessedAt is not null;
    }

    private sealed class RecordingExecutor : IRunExecutor
    {
        public ConcurrentQueue<Guid> Started { get; } = new();

        public string? Failure { get; init; }

        public Task<RunExecutionResult> ExecuteAsync(Guid runId, CancellationToken hostToken = default)
        {
            Started.Enqueue(runId);
            return Failure is null
                ? Task.FromResult(new RunExecutionResult(RunStatus.Completed))
                : Task.FromException<RunExecutionResult>(new InvalidOperationException(Failure));
        }
    }
}
