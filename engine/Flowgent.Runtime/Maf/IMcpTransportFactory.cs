namespace Flowgent.Runtime.Maf;

using ModelContextProtocol.Client;

/// <summary>
/// Builds the <see cref="IClientTransport"/> an <see cref="McpClientPool"/> connects an <see cref="McpClient"/> over.
/// Injected so tests can supply an in-memory transport (no network) while production uses
/// <see cref="HttpMcpTransportFactory"/> over a named <c>mcp</c> <see cref="System.Net.Http.IHttpClientFactory"/> client
/// (FEAT-002, design §2.3).
/// </summary>
public interface IMcpTransportFactory
{
    /// <summary>Creates a client transport for the MCP server at <paramref name="url"/> labelled <paramref name="label"/>.</summary>
    IClientTransport Create(string url, string label);
}
