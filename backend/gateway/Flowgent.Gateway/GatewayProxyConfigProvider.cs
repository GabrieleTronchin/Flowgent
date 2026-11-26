namespace Flowgent.Gateway;

using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

/// <summary>Exposes the routes generated from <see cref="GatewayOptions"/> to YARP (built once, on first use).</summary>
internal sealed class GatewayProxyConfigProvider(IOptions<GatewayOptions> options) : IProxyConfigProvider
{
    private readonly Lazy<InMemoryConfigProvider> _provider = new(() =>
    {
        var (routes, clusters) = GatewayRoutes.Build(options.Value);
        return new InMemoryConfigProvider(routes, clusters);
    });

    public IProxyConfig GetConfig() => _provider.Value.GetConfig();
}
