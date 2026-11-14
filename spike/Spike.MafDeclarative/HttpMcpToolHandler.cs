using System.Collections.Concurrent;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Spike.MafDeclarative;

/// <summary>
/// GATE-0 (FEAT-001): the HTTP analogue of <see cref="SpikeMcpToolHandler"/>.
///
/// The declarative <c>InvokeMcpTool</c> action calls
/// <see cref="IMcpToolHandler.InvokeToolAsync"/> with the <paramref name="serverUrl"/> taken
/// straight from the YAML. This handler lazily connects a real <see cref="McpClient"/> to
/// that URL over the Streamable-HTTP transport (one cached client per serverUrl) and routes
/// <c>CallToolAsync</c> through it, mapping the tool's <see cref="TextContentBlock"/> outputs
/// to <see cref="TextContent"/> for the declarative result — mirroring
/// <see cref="SpikeMcpToolHandler"/> but over HTTP.
///
/// The <see cref="HttpClient"/> is injected and NOT owned by the transport
/// (<c>ownsHttpClient: false</c>), proving the external-HttpClient
/// <see cref="HttpClientTransport"/> constructor works end to end with
/// <see cref="McpClient.CreateAsync(IClientTransport, McpClientOptions?, Microsoft.Extensions.Logging.ILoggerFactory?, CancellationToken)"/>.
/// </summary>
public sealed class HttpMcpToolHandler(HttpClient httpClient) : IMcpToolHandler, IAsyncDisposable
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ConcurrentDictionary<string, Task<McpClient>> _clients = new(StringComparer.Ordinal);

    public async Task<McpServerToolResultContent> InvokeToolAsync(
        string serverUrl,
        string? serverLabel,
        string toolName,
        IDictionary<string, object?>? arguments,
        IDictionary<string, string>? headers,
        string? connectionName,
        CancellationToken cancellationToken = default)
    {
        var argText = arguments is null
            ? "(none)"
            : string.Join(", ", arguments.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        Console.WriteLine(
            $"[HttpMcpToolHandler] InvokeToolAsync -> serverUrl='{serverUrl}' label='{serverLabel}' " +
            $"tool='{toolName}' args=[{argText}] (NO LLM in this path, over HTTP)");

        var client = await GetClientAsync(serverUrl, serverLabel, cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<string, object?>? args = arguments is null
            ? null
            : new Dictionary<string, object?>(arguments);

        var result = await client.CallToolAsync(toolName, args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var outputs = new List<AIContent>();
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text)
            {
                outputs.Add(new TextContent(text.Text));
            }
        }

        if (outputs.Count == 0 && result.StructuredContent is { } structured)
        {
            outputs.Add(new TextContent(structured.ToString()));
        }

        var callId = $"mcp-http-{Guid.NewGuid():N}";
        var content = new McpServerToolResultContent(callId)
        {
            Outputs = [.. outputs],
        };

        var preview = string.Join(" | ", content.Outputs!.OfType<TextContent>().Select(t => t.Text ?? string.Empty));
        Console.WriteLine($"[HttpMcpToolHandler] tool returned {content.Outputs.Count} content block(s): \"{preview}\"");
        return content;
    }

    private Task<McpClient> GetClientAsync(string serverUrl, string? serverLabel, CancellationToken ct) =>
        _clients.GetOrAdd(serverUrl, url => CreateClientAsync(url, serverLabel, ct));

    private async Task<McpClient> CreateClientAsync(string serverUrl, string? serverLabel, CancellationToken ct)
    {
        Console.WriteLine($"[HttpMcpToolHandler] connecting McpClient to '{serverUrl}' (HttpClientTransport, external HttpClient)");

        // External, caller-owned HttpClient: ownsHttpClient:false. This is exactly the
        // injectable-HttpClient constructor the later engine work depends on.
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(serverUrl),
                Name = serverLabel ?? serverUrl,
            },
            _httpClient,
            loggerFactory: null,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var task in _clients.Values)
        {
            try
            {
                var client = await task.ConfigureAwait(false);
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch { /* best-effort cleanup in a spike */ }
        }
    }
}
