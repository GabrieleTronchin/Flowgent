using System.IO.Pipelines;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Spike.MafDeclarative;

/// <summary>
/// GOAL 3: an MCP tool invoked inside the workflow.
///
/// This spins up a REAL MCP server (official ModelContextProtocol C# SDK) exposing
/// one trivial tool, "wordCount", and connects a REAL McpClient to it. To stay fully
/// offline and deterministic the two endpoints are wired over in-memory pipes
/// (System.IO.Pipelines) with StreamServerTransport / StreamClientTransport -- no
/// child process, no HTTP port, no network. See README Findings for why this path
/// was chosen over a self-hosted HTTP MCP server.
///
/// The client's tools are returned as Microsoft.Extensions.AI AIFunction instances,
/// which we hand to the agent via ResponseAgentProvider.Functions so the declarative
/// workflow's agent turn actually calls the MCP tool.
/// </summary>
public sealed class McpWordCountServer : IAsyncDisposable
{
    private readonly McpServer _server;
    private readonly McpClient _client;
    private readonly Task _serverLoop;

    private McpWordCountServer(McpServer server, McpClient client, Task serverLoop)
    {
        _server = server;
        _client = client;
        _serverLoop = serverLoop;
    }

    public static async Task<McpWordCountServer> StartAsync(CancellationToken cancellationToken = default)
    {
        Pipe clientToServer = new();
        Pipe serverToClient = new();

        // The one trivial tool: count whitespace-separated words in the input text.
        var wordCountTool = McpServerTool.Create(
            (string text) =>
            {
                var count = string.IsNullOrWhiteSpace(text)
                    ? 0
                    : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                Console.WriteLine($"[MCP server] wordCount invoked: text=\"{text}\" -> {count}");
                return count;
            },
            new McpServerToolCreateOptions
            {
                Name = "wordCount",
                Description = "Counts the number of whitespace-separated words in the provided text.",
            });

        var options = new McpServerOptions
        {
            ServerInfo = new() { Name = "spike-wordcount", Version = "1.0.0" },
            ToolCollection = [wordCountTool],
        };

        var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());

        var server = McpServer.Create(serverTransport, options);
        var serverLoop = server.RunAsync(CancellationToken.None);

        var clientTransport = new StreamClientTransport(
            clientToServer.Writer.AsStream(),
            serverToClient.Reader.AsStream());

        var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new McpWordCountServer(server, client, serverLoop);
    }

    /// <summary>Returns the MCP server's tools as AIFunctions (one per tool).</summary>
    public async Task<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"[MCP client] ListToolsAsync returned {tools.Count} tool(s): {string.Join(", ", tools.Select(t => t.Name))}");
        // McpClientTool derives from AIFunction.
        return tools.Cast<AIFunction>().ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        await _server.DisposeAsync().ConfigureAwait(false);
        try { await _serverLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch { /* best-effort cleanup in a spike */ }
    }
}
