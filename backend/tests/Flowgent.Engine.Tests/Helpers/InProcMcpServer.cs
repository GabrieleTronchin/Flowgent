namespace Flowgent.Engine.Tests.Helpers;

using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// A real in-process MCP server wired to the client over in-memory pipes (no network, no port), used by the FEAT-002
/// handler/pool tests. It exposes one tool whose behaviour the test controls (the returned text, an <c>isError</c>
/// result, or a call counter). Mirrors the spike's in-memory server, but the client half is created by the pool under
/// test; this type only produces the <see cref="IClientTransport"/> the pool connects over.
/// </summary>
public sealed class InProcMcpServer : IAsyncDisposable
{
    private readonly McpServer _server;
    private readonly Task _serverLoop;

    public InProcMcpServer(string toolName, Func<string?, ToolReply> handler)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        Calls = 0;
        var tool = McpServerTool.Create(
            (string? text = null) =>
            {
                Calls++;
                LastText = text;
                var reply = handler(text);
                return new CallToolResult
                {
                    IsError = reply.IsError,
                    Content = [new TextContentBlock { Text = reply.Text }],
                };
            },
            new McpServerToolCreateOptions { Name = toolName });

        var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());

        _server = McpServer.Create(serverTransport, new McpServerOptions
        {
            ServerInfo = new() { Name = "test-mcp", Version = "1.0.0" },
            ToolCollection = [tool],
        });
        _serverLoop = _server.RunAsync(CancellationToken.None);

        ClientTransport = new StreamClientTransport(
            clientToServer.Writer.AsStream(),
            serverToClient.Reader.AsStream());
    }

    /// <summary>The transport a client connects to this server over.</summary>
    public IClientTransport ClientTransport { get; }

    /// <summary>Number of times the tool was invoked.</summary>
    public int Calls { get; private set; }

    /// <summary>The bound <c>text</c> argument of the most recent tool invocation.</summary>
    public string? LastText { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _serverLoop.ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: the loop ends on dispose; a cancellation/EOF here is expected.
        }
    }

    /// <summary>A scripted tool reply: the single text payload and whether it is an error result.</summary>
    public readonly record struct ToolReply(string Text, bool IsError = false);
}
