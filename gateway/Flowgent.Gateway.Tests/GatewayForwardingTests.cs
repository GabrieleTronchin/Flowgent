namespace Flowgent.Gateway.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flowgent.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

public sealed class GatewayForwardingTests : IAsyncLifetime
{
    private readonly HostRoutingHandler _router = new();
    private WebApplication _engine = null!;
    private WebApplication _support = null!;
    private WebApplication _gateway = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _engine = await StartEchoServiceAsync("engine");
        _support = await StartEchoServiceAsync("support");
        _router.Register("engine", _engine.GetTestServer().CreateHandler());
        _router.Register("support", _support.GetTestServer().CreateHandler());

        _gateway = GatewayHost.Create(new() { EnvironmentName = "Testing" }, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Engine:Address"] = "http://engine",
                ["Gateway:Modules:0:Id"] = "support",
                ["Gateway:Modules:0:Address"] = "http://support",
                ["Gateway:Modules:0:ExtraRoutes:0"] = "/api/support/{**catch-all}",
            });
            builder.Services.AddSingleton<IForwarderHttpClientFactory>(new TestForwarderHttpClientFactory(_router));
        });
        await _gateway.StartAsync();
        _client = _gateway.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _gateway.DisposeAsync();
        await _support.DisposeAsync();
        await _engine.DisposeAsync();
    }

    [Theory]
    [InlineData("/api/engine/runs", "engine")]
    [InlineData("/flowgent.RunMonitor/Subscribe", "engine")]
    [InlineData("/api/modules/support/emails", "support")]
    [InlineData("/api/support/emails", "support")]
    public async Task Request_IsForwardedToTheOwningService(string path, string expectedService)
    {
        var echo = await _client.GetFromJsonAsync<Echo>(path, TestContext.Current.CancellationToken);

        Assert.Equal(expectedService, echo!.Service);
        Assert.Equal(path, echo.Path);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/api/modules/food/x", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CorrelationId_IsCreatedAndForwarded()
    {
        var response = await _client.GetAsync("/api/engine/runs", TestContext.Current.CancellationToken);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken);

        var returned = Assert.Single(response.Headers.GetValues(GatewayMiddleware.CorrelationIdHeader));
        Assert.False(string.IsNullOrWhiteSpace(returned));
        Assert.Equal(returned, echo!.CorrelationId);
    }

    [Fact]
    public async Task CorrelationId_FromCallerIsPreserved()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/support/emails");
        request.Headers.Add(GatewayMiddleware.CorrelationIdHeader, "abc-123");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken);

        Assert.Equal("abc-123", echo!.CorrelationId);
        Assert.Equal("abc-123", Assert.Single(response.Headers.GetValues(GatewayMiddleware.CorrelationIdHeader)));
    }

    [Theory]
    [InlineData("/api/engine/runs", "engine")]
    [InlineData("/api/support/emails", "support")]
    public async Task UnavailableService_Returns503ProblemNamingTheService(string path, string service)
    {
        _router.SetDown(service);

        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(service, problem.RootElement.GetProperty("service").GetString());
        Assert.Contains($"'{service}'", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Health_IsServedByTheGateway()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<WebApplication> StartEchoServiceAsync(string name)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.Run(context => context.Response.WriteAsJsonAsync(new Echo(
            name,
            context.Request.Path,
            context.Request.Headers[GatewayMiddleware.CorrelationIdHeader].FirstOrDefault())));
        await app.StartAsync();
        return app;
    }

    private sealed record Echo(string Service, string Path, string? CorrelationId);
}
