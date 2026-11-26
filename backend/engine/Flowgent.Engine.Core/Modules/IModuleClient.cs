namespace Flowgent.Engine.Core.Modules;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Microsoft.Extensions.Options;

/// <summary>Calls module services through the Module SDK contracts (package and tools).</summary>
public interface IModuleClient
{
    Task<PackageFetchResult> FetchPackageAsync(string moduleId, string? knownVersion, CancellationToken ct);

    Task<JsonNode?> InvokeToolAsync(string moduleId, string toolName, ToolInvocationRequest request, TimeSpan timeout, CancellationToken ct);
}
