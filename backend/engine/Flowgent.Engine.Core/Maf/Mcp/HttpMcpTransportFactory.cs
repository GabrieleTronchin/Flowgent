namespace Flowgent.Engine.Core.Maf.Mcp;

using ModelContextProtocol.Client;

/// <summary>
/// Production <see cref="IMcpTransportFactory"/>: a Streamable-HTTP <see cref="HttpClientTransport"/> over the named
/// <c>mcp</c> <see cref="IHttpClientFactory"/> client. The <see cref="System.Net.Http.HttpClient"/> is owned by the
/// factory, not the transport (<c>ownsHttpClient: false</c>), so pooling/resilience stay with the factory.
/// </summary>
public sealed class HttpMcpTransportFactory(IHttpClientFactory httpClientFactory) : IMcpTransportFactory
{
    public const string HttpClientName = "mcp";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public IClientTransport Create(string url, string label) =>
        new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(url),
                Name = label,
            },
            _httpClientFactory.CreateClient(HttpClientName),
            loggerFactory: null,
            ownsHttpClient: false);
}
