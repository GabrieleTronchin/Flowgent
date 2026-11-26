namespace Flowgent.Gateway;

using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

/// <summary>
/// Builds the gateway application. <c>Program.cs</c> and the in-process test harness both use it.
/// </summary>
public static class GatewayHost
{
    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        builder.AddServiceDefaults();

        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.AddSingleton<IProxyConfigProvider, GatewayProxyConfigProvider>();
        builder.Services.AddProblemDetails();
        builder.Services.AddReverseProxy().AddServiceDiscoveryDestinationResolver();

        configure?.Invoke(builder);

        var app = builder.Build();

        app.UseCorrelationId();
        app.MapDefaultEndpoints();
        app.MapReverseProxy(proxy => proxy.Use(GatewayMiddleware.WriteServiceUnavailableOnForwarderError));

        return app;
    }
}

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
