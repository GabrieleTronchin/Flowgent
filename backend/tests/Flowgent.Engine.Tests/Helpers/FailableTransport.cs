namespace Flowgent.Engine.Tests.Helpers;

using System.Threading.Channels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// An <see cref="IClientTransport"/> decorator that forwards to a real inner transport but can be told to fail every
/// subsequent <see cref="ITransport.SendMessageAsync"/> with an <see cref="IOException"/> — simulating a broken MCP
/// session at call time (after a successful connect/initialize) so the handler's evict-and-reconnect path can be
/// exercised deterministically without killing a real server mid-flight.
/// </summary>
public sealed class FailableTransport(IClientTransport inner) : IClientTransport
{
    private readonly IClientTransport _inner = inner;

    /// <summary>When true, the connected session throws on the next (and every) send.</summary>
    public bool FailSends { get; set; }

    public string Name => _inner.Name;

    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var session = await _inner.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return new Session(session, this);
    }

    private sealed class Session(ITransport inner, FailableTransport owner) : ITransport
    {
        public string? SessionId => inner.SessionId;

        public ChannelReader<JsonRpcMessage> MessageReader => inner.MessageReader;

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
            owner.FailSends
                ? throw new IOException("simulated MCP session failure")
                : inner.SendMessageAsync(message, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
