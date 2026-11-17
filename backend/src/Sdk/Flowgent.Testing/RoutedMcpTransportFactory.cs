namespace Flowgent.Testing;

using Flowgent.Runtime.Maf;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

/// <summary>
/// The in-process test counterpart of <c>HttpMcpTransportFactory</c>: it builds a Streamable-HTTP
/// <see cref="HttpClientTransport"/> whose <see cref="System.Net.Http.HttpClient"/> routes through the harness
/// <see cref="HostRoutingHandler"/> (so <c>http://{id}</c> reaches the MCP server's TestServer), with
/// <c>ownsHttpClient:false</c> — the shape Gate-0/FEAT-001 proved end to end. Registered in the engine the harness
/// builds, overriding the production <see cref="IMcpTransportFactory"/>, so <c>SupportParityTests</c> exercises
/// <c>locate_component</c> over in-process HTTP without a real port.
/// </summary>
public sealed class RoutedMcpTransportFactory(HostRoutingHandler router, ILoggerFactory loggerFactory) : IMcpTransportFactory
{
    private readonly HttpClient _httpClient = new(router.CreateClientHandler());
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public IClientTransport Create(string url, string label) =>
        new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(url),
                Name = label,
            },
            _httpClient,
            _loggerFactory,
            ownsHttpClient: false);
}
