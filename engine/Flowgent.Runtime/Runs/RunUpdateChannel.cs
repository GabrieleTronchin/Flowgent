namespace Flowgent.Runtime.Runs;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Flowgent.Contracts.Runs;

/// <summary>A run transition published to stream subscribers (gRPC).</summary>
public sealed record RunUpdate(
    Guid RunId,
    string ModuleId,
    string WorkflowId,
    string? StepId,
    RunStatus Status,
    RunEventKind Kind,
    DateTimeOffset Timestamp,
    string? Detail,
    string? Title);

/// <summary>
/// Fans run updates out to every subscriber. Each subscriber has its own bounded channel (oldest updates are
/// dropped when it falls behind), so a slow client never blocks the engine or other clients.
/// </summary>
public sealed class RunUpdateChannel
{
    private const int SubscriberCapacity = 1000;
    private readonly ConcurrentDictionary<Guid, Channel<RunUpdate>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public void Publish(RunUpdate update)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(update);
        }
    }

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<RunUpdate>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _subscribers[id] = channel;
        return new Subscription(channel.Reader, () =>
        {
            if (_subscribers.TryRemove(id, out var removed))
            {
                removed.Writer.TryComplete();
            }
        });
    }

    public sealed class Subscription(ChannelReader<RunUpdate> reader, Action unsubscribe) : IDisposable
    {
        public ChannelReader<RunUpdate> Reader { get; } = reader;

        public void Dispose() => unsubscribe();
    }
}
