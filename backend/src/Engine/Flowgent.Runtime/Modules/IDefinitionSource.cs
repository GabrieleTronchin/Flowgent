namespace Flowgent.Runtime.Modules;

/// <summary>
/// A source of module definition packages (design §4.2). <see cref="VolumeDefinitionSource"/> (primary) reads a
/// mounted folder; <see cref="HttpDefinitionSource"/> (secondary) wraps the pull-model HTTP client. Both return the
/// existing <see cref="PackageFetchResult"/> so <c>ModuleCatalog</c>'s version-swap and conflict logic is unchanged.
/// </summary>
public interface IDefinitionSource
{
    string Name { get; }

    Task<PackageFetchResult> FetchAsync(string moduleId, string? knownVersion, CancellationToken ct);
}
