namespace Flowgent.Engine.Tests.Helpers;

using Flowgent.Runtime.Maf;
using ModelContextProtocol.Client;

/// <summary>
/// A test <see cref="IMcpTransportFactory"/> that hands out a scripted sequence of client transports (no network). Each
/// <see cref="Create"/> call returns the next transport in the queue, recording the (url, label) it was asked for so a
/// test can assert the connect count (e.g. an evict-and-reconnect produces exactly two <see cref="Create"/> calls).
/// </summary>
public sealed class FakeMcpTransportFactory : IMcpTransportFactory
{
    private readonly Queue<IClientTransport> _transports;

    public FakeMcpTransportFactory(params IClientTransport[] transports)
    {
        _transports = new Queue<IClientTransport>(transports);
    }

    public List<(string Url, string Label)> Creates { get; } = [];

    public IClientTransport Create(string url, string label)
    {
        Creates.Add((url, label));
        if (_transports.Count == 0)
        {
            throw new InvalidOperationException("FakeMcpTransportFactory ran out of scripted transports.");
        }

        return _transports.Dequeue();
    }
}
