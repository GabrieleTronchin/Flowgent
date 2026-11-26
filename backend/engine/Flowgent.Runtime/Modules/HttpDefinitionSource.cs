namespace Flowgent.Runtime.Modules;

/// <summary>
/// Secondary definition source (design §4.1): wraps the existing pull-model <see cref="IModuleClient.FetchPackageAsync"/>
/// (ETag <c>If-None-Match</c>) with no behaviour change.
/// </summary>
public sealed class HttpDefinitionSource(IModuleClient client) : IDefinitionSource
{
    public string Name => "http";

    public Task<PackageFetchResult> FetchAsync(string moduleId, string? knownVersion, CancellationToken ct) =>
        client.FetchPackageAsync(moduleId, knownVersion, ct);
}
