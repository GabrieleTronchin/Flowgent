namespace Flowgent.Runtime.Modules;

using Flowgent.Definitions;
using Flowgent.Runtime.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Primary definition source (design §4.1): reads a module's definition folder from a mounted volume
/// (<c>Flowgent:Definitions:Path</c>) and builds a <c>ModulePackage</c> with the same
/// <see cref="ModuleLoader.ReadPackage"/>/<see cref="ModuleLoader.ComputeVersion"/> used by the HTTP path, so the
/// version/ETag semantics are byte-identical. Returns <c>NotModified</c> when the computed version matches the
/// known one, else <c>Changed</c>.
/// </summary>
public sealed class VolumeDefinitionSource(IOptions<EngineOptions> options) : IDefinitionSource
{
    public string Name => "volume";

    /// <summary>True when a volume path is configured and the given module's folder exists under it.</summary>
    public bool HasModule(string moduleId) => ModuleFolder(moduleId) is { } folder && Directory.Exists(folder);

    public Task<PackageFetchResult> FetchAsync(string moduleId, string? knownVersion, CancellationToken ct)
    {
        var folder = ModuleFolder(moduleId);
        if (folder is null || !Directory.Exists(folder))
        {
            return Task.FromResult<PackageFetchResult>(new PackageFetchResult.Failed($"Definition folder for module '{moduleId}' was not found on the volume."));
        }

        try
        {
            var package = ModuleLoader.ReadPackage(folder, moduleId);
            return Task.FromResult<PackageFetchResult>(knownVersion is not null && knownVersion == package.Version
                ? new PackageFetchResult.NotModified()
                : new PackageFetchResult.Changed(package));
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return Task.FromResult<PackageFetchResult>(new PackageFetchResult.Failed(ex.Message));
        }
    }

    private string? ModuleFolder(string moduleId)
    {
        var root = options.Value.Definitions.Path;
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, moduleId);
    }
}
