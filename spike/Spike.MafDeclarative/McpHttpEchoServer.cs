using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Spike.MafDeclarative;

/// <summary>
/// GATE-0 (FEAT-001): a REAL in-process HTTP MCP server using
/// <c>ModelContextProtocol.AspNetCore</c>. This is the HTTP analogue of the in-memory-pipe
/// <see cref="McpWordCountServer"/>: a minimal <see cref="WebApplication"/> that registers
/// an MCP server with the Streamable-HTTP transport and maps it with <c>app.MapMcp()</c>.
///
/// It binds Kestrel to <c>http://127.0.0.1:0</c> (port 0 = let the OS pick a free port),
/// starts the host, then reads the actually-bound address back via
/// <see cref="IServerAddressesFeature"/> so the client/handler can target the real URL. No
/// fixed port, no network beyond loopback: fully offline and deterministic.
///
/// The single tool <c>echoUpper(text)</c> returns the uppercased input as a NON-numeric
/// string (spike finding F4: a bare JSON number crosses the declarative InvokeMcpTool
/// PowerFx binding as System.Double and throws "Unsupported element type: Double", so the
/// tool result must be a non-numeric string).
/// </summary>
public sealed class McpHttpEchoServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private McpHttpEchoServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>The real bound base URL, e.g. <c>http://127.0.0.1:52431</c>.</summary>
    public string BaseUrl { get; }

    /// <summary>The MCP endpoint URL the client/handler should target (MapMcp root).</summary>
    public string McpUrl => BaseUrl;

    public static async Task<McpHttpEchoServer> StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Keep the spike quiet and offline: no console logging spam from Kestrel/hosting.
        builder.Logging.ClearProviders();

        // Register a real MCP server over the Streamable-HTTP transport, exposing echoUpper.
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<EchoUpperTool>();

        // Port 0 -> OS picks a free loopback port; we read it back after Start.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.MapMcp();

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        // Read back the actually-bound address.
        var addresses = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var bound = addresses?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("MCP HTTP server did not report a bound address.");

        Console.WriteLine($"[MCP HTTP server] started, bound to {bound} (echoUpper tool, offline loopback)");
        return new McpHttpEchoServer(app, bound.TrimEnd('/'));
    }

    public async ValueTask DisposeAsync()
    {
        try { await _app.StopAsync().ConfigureAwait(false); }
        catch { /* best-effort cleanup in a spike */ }
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The one deterministic MCP tool exposed over HTTP. Returns the uppercased input as a
    /// NON-numeric string (prefixed) so it never crosses the declarative PowerFx binding as
    /// a bare number (spike finding F4).
    /// </summary>
    [McpServerToolType]
    public sealed class EchoUpperTool
    {
        [McpServerTool(Name = "echoUpper")]
        [Description("Returns the provided text uppercased, as a non-numeric string.")]
        public static string EchoUpper([Description("The text to uppercase.")] string text)
        {
            var result = (text ?? string.Empty).ToUpperInvariant();
            Console.WriteLine($"[MCP HTTP server] echoUpper invoked: text=\"{text}\" -> \"{result}\"");
            return result;
        }
    }
}
