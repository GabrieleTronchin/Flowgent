namespace Flowgent.Runtime.Maf;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

/// <summary>
/// Caches one connected <see cref="McpClient"/> per resolved MCP server URL. First-connect to a
/// URL is guarded by a per-URL <see cref="Lazy{T}"/> of a <see cref="Task{TResult}"/> so concurrent callers share one
/// session; a connect failure is never cached (the failed entry is removed, so the next caller retries). The pool is
/// the only place the MCP client is created or disposed, so the handler can <see cref="Evict"/> a broken session and
/// ask for a fresh one.
/// </summary>
public sealed class McpClientPool(IMcpTransportFactory transportFactory) : IAsyncDisposable
{
    private readonly IMcpTransportFactory _transportFactory = transportFactory;
    private readonly ConcurrentDictionary<string, Lazy<Task<McpClient>>> _clients = new(StringComparer.Ordinal);


    /// <summary>
    /// Returns the cached client for <paramref name="url"/>, connecting one on first use. On a connect failure the
    /// entry is evicted (never cached) and the exception propagates so the caller can surface it.
    /// </summary>
    public async Task<McpClient> GetAsync(string url, string label, CancellationToken ct)
    {
        var lazy = _clients.GetOrAdd(url, key => new Lazy<Task<McpClient>>(() => ConnectAsync(key, label, ct)));
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // Never cache a failed client: drop this exact entry so the next caller retries a fresh connect.
            _clients.TryRemove(new KeyValuePair<string, Lazy<Task<McpClient>>>(url, lazy));
            throw;
        }
    }

    /// <summary>Drops the cached client for <paramref name="url"/> (e.g. after a session/transport failure) and disposes it best-effort.</summary>
    public void Evict(string url)
    {
        if (_clients.TryRemove(url, out var lazy))
        {
            _ = DisposeLazyAsync(lazy);
        }
    }

    private async Task<McpClient> ConnectAsync(string url, string label, CancellationToken ct)
    {
        var transport = _transportFactory.Create(url, label);
        return await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
    }

    private static async Task DisposeLazyAsync(Lazy<Task<McpClient>> lazy)
    {
        try
        {
            var client = await lazy.Value.ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup: a client that failed to connect or is already broken has nothing to dispose.
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _clients.Values)
        {
            await DisposeLazyAsync(lazy).ConfigureAwait(false);
        }

        _clients.Clear();
    }
}
