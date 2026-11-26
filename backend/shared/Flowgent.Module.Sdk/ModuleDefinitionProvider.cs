namespace Flowgent.Module.Sdk;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// The module's definition folder read once at startup: the package served to the engine and the
/// validation result (same rules as the engine).
/// </summary>
public sealed class ModuleDefinitionProvider
{
    private readonly Lazy<(ModulePackage Package, LoadResult Result)> _loaded;

    public ModuleDefinitionProvider(IOptions<ModuleSdkOptions> options, IHostEnvironment environment)
    {
        var path = options.Value.DefinitionPath;
        DefinitionRoot = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
        _loaded = new(() =>
        {
            var package = ModuleLoader.ReadPackage(DefinitionRoot);
            return (package, ModuleLoader.Load(package));
        });
    }

    /// <summary>Full path of the <c>definition/</c> folder.</summary>
    public string DefinitionRoot { get; }

    /// <summary>The module folder (parent of <c>definition/</c>), e.g. for module assets such as dummy apps.</summary>
    public string ModuleRoot => Path.GetDirectoryName(DefinitionRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;

    public ModulePackage Package => _loaded.Value.Package;

    public LoadResult LoadResult => _loaded.Value.Result;

    public string ModuleId => LoadResult.Module?.Id ?? Package.ModuleId;

    /// <summary>The module <c>config</c> section (empty when the definition is invalid).</summary>
    public JsonObject Config => LoadResult.Module?.Config ?? [];
}
