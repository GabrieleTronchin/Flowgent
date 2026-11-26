namespace Flowgent.Gateway;

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
