namespace Flowgent.Engine.Tests.Runtime;

using Flowgent.Engine.Tests.Helpers;
using Flowgent.Runtime.Maf;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

/// <summary>
/// FEAT-002 unit tests for <see cref="McpClientPool"/>: one cached client per URL, a single session when callers race
/// the first connect, and <see cref="McpClientPool.Evict"/> dropping the cached client. All over in-memory pipes.
/// </summary>
public sealed class McpClientPoolTests
{
    private const string Url = "http://pool-mcp";
    private const string Label = "demo";

    [Fact]
    public async Task GetAsync_CachesOneClientPerUrl()
    {
        await using var server = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{}"));
        var factory = new FakeMcpTransportFactory(server.ClientTransport);
        await using var pool = new McpClientPool(factory, NullLoggerFactory.Instance);

        var first = await pool.GetAsync(Url, Label, CancellationToken.None);
        var second = await pool.GetAsync(Url, Label, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Single(factory.Creates); // only one transport/connect for the same URL
    }

    [Fact]
    public async Task GetAsync_ConcurrentFirstConnect_OpensExactlyOneSession()
    {
        await using var server = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{}"));
        var factory = new FakeMcpTransportFactory(server.ClientTransport);
        await using var pool = new McpClientPool(factory, NullLoggerFactory.Instance);

        var tasks = Enumerable.Range(0, 8).Select(_ => pool.GetAsync(Url, Label, CancellationToken.None)).ToArray();
        var clients = await Task.WhenAll(tasks);

        Assert.All(clients, c => Assert.Same(clients[0], c));
        Assert.Single(factory.Creates);
    }

    [Fact]
    public async Task Evict_DropsTheCachedClient_SoNextGetReconnects()
    {
        await using var serverA = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{}"));
        await using var serverB = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{}"));
        var factory = new FakeMcpTransportFactory(serverA.ClientTransport, serverB.ClientTransport);
        await using var pool = new McpClientPool(factory, NullLoggerFactory.Instance);

        var first = await pool.GetAsync(Url, Label, CancellationToken.None);
        pool.Evict(Url);
        var second = await pool.GetAsync(Url, Label, CancellationToken.None);

        Assert.NotSame(first, second);
        Assert.Equal(2, factory.Creates.Count);
    }

    [Fact]
    public async Task GetAsync_DoesNotCacheAFailedClient()
    {
        await using var healthy = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{}"));
        // First connect has no transport (factory throws); the second succeeds.
        var factory = new ThrowOnceTransportFactory(healthy.ClientTransport);
        await using var pool = new McpClientPool(factory, NullLoggerFactory.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => pool.GetAsync(Url, Label, CancellationToken.None));
        var client = await pool.GetAsync(Url, Label, CancellationToken.None); // retries, now succeeds

        Assert.NotNull(client);
        Assert.Equal(2, factory.Creates.Count);
    }

    private sealed class ThrowOnceTransportFactory(IClientTransport onSuccess) : IMcpTransportFactory
    {
        private int _calls;

        public List<(string Url, string Label)> Creates { get; } = [];

        public IClientTransport Create(string url, string label)
        {
            Creates.Add((url, label));
            return _calls++ == 0 ? throw new InvalidOperationException("boom") : onSuccess;
        }
    }
}
