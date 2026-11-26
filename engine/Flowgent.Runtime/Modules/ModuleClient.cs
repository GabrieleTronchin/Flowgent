namespace Flowgent.Runtime.Modules;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Runtime.Configuration;
using Microsoft.Extensions.Options;

/// <summary>Result of a package request.</summary>
public abstract record PackageFetchResult
{
    public sealed record Changed(ModulePackage Package) : PackageFetchResult;

    public sealed record NotModified : PackageFetchResult;

    public sealed record Failed(string Error) : PackageFetchResult;
}

/// <summary>A tool call failed; <see cref="Exception.Message"/> is suitable for the run detail.</summary>
public sealed class ToolInvocationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Calls module services through the Module SDK contracts (package and tools).</summary>
public interface IModuleClient
{
    Task<PackageFetchResult> FetchPackageAsync(string moduleId, string? knownVersion, CancellationToken ct);

    Task<JsonNode?> InvokeToolAsync(string moduleId, string toolName, ToolInvocationRequest request, TimeSpan timeout, CancellationToken ct);
}

internal sealed class ModuleClient(IHttpClientFactory httpClientFactory, IOptions<EngineOptions> options) : IModuleClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PackageFetchResult> FetchPackageAsync(string moduleId, string? knownVersion, CancellationToken ct)
    {
        var module = GetModule(moduleId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, module.TimeoutSeconds)));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Combine(module.Url, ModuleRoutes.Package));
            if (knownVersion is not null)
            {
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{knownVersion}\""));
            }

            using var response = await CreateClient(moduleId).SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new PackageFetchResult.NotModified();
            }

            if (!response.IsSuccessStatusCode)
            {
                return new PackageFetchResult.Failed($"GET {ModuleRoutes.Package} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var package = await response.Content.ReadFromJsonAsync<ModulePackage>(JsonOptions, timeout.Token);
            return package is null
                ? new PackageFetchResult.Failed("The module returned an empty package.")
                : new PackageFetchResult.Changed(package);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PackageFetchResult.Failed($"The module did not answer within {module.TimeoutSeconds}s.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            return new PackageFetchResult.Failed(ex.Message);
        }
    }

    public async Task<JsonNode?> InvokeToolAsync(string moduleId, string toolName, ToolInvocationRequest request, TimeSpan timeout, CancellationToken ct)
    {
        var module = GetModule(moduleId);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            using var response = await CreateClient(moduleId).PostAsJsonAsync(
                Combine(module.Url, ModuleRoutes.Tool(toolName)), request, JsonOptions, timeoutCts.Token);
            var body = await ReadResponseAsync(response, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var error = body?.Error ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
                throw new ToolInvocationException($"Tool '{toolName}' failed: {error}");
            }

            return body is { Error: { } bodyError }
                ? throw new ToolInvocationException($"Tool '{toolName}' failed: {bodyError}")
                : body?.Output;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ToolInvocationException($"Tool '{toolName}' timed out after {timeout.TotalSeconds:0.#}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new ToolInvocationException($"Module '{moduleId}' is unavailable: {ex.Message}", ex);
        }
    }

    private static async Task<ToolInvocationResponse?> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ToolInvocationResponse>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private HttpClient CreateClient(string moduleId) => httpClientFactory.CreateClient(ModuleEndpointOptions.HttpClientName(moduleId));

    private ModuleEndpointOptions GetModule(string moduleId) =>
        options.Value.Modules.FirstOrDefault(m => m.Id == moduleId && m.Enabled)
        ?? throw new InvalidOperationException($"Module '{moduleId}' is not configured.");

    private static Uri Combine(string baseUrl, string path) => new(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
}
