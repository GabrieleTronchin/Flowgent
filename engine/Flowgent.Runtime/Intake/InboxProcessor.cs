namespace Flowgent.Runtime.Intake;

using System.Collections.Concurrent;
using Flowgent.Runtime.Configuration;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Runs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Polls the inbox and starts the run of each pending message in FIFO order (ReceivedAt). Runs can last long
/// (e.g. waiting for human input), so the loop only starts them and never waits: a paused run cannot block the inbox.
/// Messages are always marked processed (no infinite retries); start errors are recorded on the message.
/// </summary>
public sealed class InboxProcessor(
    IInboxStore inbox,
    IRunStore runs,
    IRunExecutor orchestrator,
    IOptions<EngineOptions> options,
    ILogger<InboxProcessor> logger) : BackgroundService
{
    private readonly SemaphoreSlim _dispatchLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Task> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0.01, options.Value.InboxPollingIntervalSeconds));
        logger.LogInformation("InboxProcessor started with polling interval {Interval}s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "InboxProcessor cycle failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Let in-flight runs observe cancellation and record their outcome before shutdown.
        await Task.WhenAll(_inFlight.Values);
        logger.LogInformation("InboxProcessor stopped");
    }

    /// <summary>Runs one polling cycle and waits for the runs it started (tests).</summary>
    public async Task ProcessPendingMessagesAsync(CancellationToken ct = default) =>
        await Task.WhenAll(await DispatchPendingMessagesAsync(ct));

    /// <summary>Runs one polling cycle without waiting for the started runs.</summary>
    public async Task<IReadOnlyList<Task>> DispatchPendingMessagesAsync(CancellationToken ct = default)
    {
        await _dispatchLock.WaitAsync(ct);
        try
        {
            var toStart = await SelectMessagesToStartAsync(ct);
            var messages = new List<InboxMessageRecord>(toStart.Count);
            foreach (var messageId in toStart)
            {
                if (await inbox.GetAsync(messageId, CancellationToken.None) is { ProcessedAt: null } message)
                {
                    messages.Add(message);
                }
            }

            var started = new List<Task>(messages.Count);
            foreach (var message in messages)
            {
                // Started one after the other (the executor is invoked before the first await),
                // so runs are invoked in FIFO order; they then run concurrently.
                var task = StartMessageAsync(message, ct);
                _inFlight[message.Id] = task;
                started.Add(task);
                var messageId = message.Id;
                _ = task.ContinueWith(_ => _inFlight.TryRemove(messageId, out Task? _), TaskScheduler.Default);
            }

            return started;
        }
        finally
        {
            _dispatchLock.Release();
        }
    }

    private async Task<List<Guid>> SelectMessagesToStartAsync(CancellationToken ct)
    {
        var pending = await inbox.GetPendingIdsAsync(ct);
        var toStart = pending.Where(id => !_inFlight.ContainsKey(id)).ToList();

        if (!options.Value.SequentialProcessing || toStart.Count == 0)
        {
            return toStart;
        }

        // Sequential mode: one run at a time, wait until the previous one has finished.
        if (!_inFlight.IsEmpty || await runs.AnyActiveAsync(ct))
        {
            return [];
        }

        return [toStart[0]];
    }

    private Task StartMessageAsync(InboxMessageRecord message, CancellationToken ct)
    {
        Task<RunExecutionResult> execution;
        try
        {
            execution = message.RunId is { } runId
                ? orchestrator.ExecuteAsync(runId, ct)
                : Task.FromException<RunExecutionResult>(new InvalidOperationException("The inbox message has no run."));
        }
        catch (Exception ex)
        {
            execution = Task.FromException<RunExecutionResult>(ex);
        }

        return CompleteMessageAsync(message.Id, execution);
    }

    private async Task CompleteMessageAsync(Guid messageId, Task<RunExecutionResult> execution)
    {
        string? error;
        try
        {
            error = (await execution).StartError;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogWarning(ex, "Failed to process inbox message {Id}", messageId);
        }

        // Always mark as processed (no infinite retries), even during shutdown.
        await inbox.MarkProcessedAsync(messageId, error, CancellationToken.None);
    }
}
