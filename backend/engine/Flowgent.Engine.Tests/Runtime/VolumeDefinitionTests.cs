namespace Flowgent.Engine.Tests.Runtime;

using Flowgent.Contracts.Api;
using Flowgent.Engine.Core.Modules.Catalog;
using Flowgent.Engine.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The engine image reads module definitions from a mounted folder (<c>Flowgent:Definitions:Path</c>,
/// <c>/definitions</c> in the container): one sub-folder per module id, no module service needed.
/// </summary>
public class VolumeDefinitionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowgent-volume-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ModuleFolderOnVolume_LoadsWithoutModuleUrl_AndSurvivesTheModuleServiceBeingDown()
    {
        WriteModule("demo");

        // No Url for the module: a volume module does not need a module service.
        await using var engine = await TestEngine.StartAsync(configuration: VolumeConfig(url: ""));
        var catalog = engine.Services.GetRequiredService<IModuleCatalog>();
        Assert.Equal(ModuleStatus.Available, catalog.FindModule("demo")!.Status);

        // The HTTP source would report the module as unavailable; the volume source does not depend on it.
        engine.Modules.DownModules.Add("demo");
        await engine.Services.GetRequiredService<ModuleCatalogService>().RefreshAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ModuleStatus.Available, catalog.FindModule("demo")!.Status);
        Assert.NotNull(catalog.FindWorkflow("hello"));
    }

    [Fact]
    public async Task ChangedFilesOnVolume_AreLoadedOnTheNextRefresh()
    {
        WriteModule("demo");
        await using var engine = await TestEngine.StartAsync(configuration: VolumeConfig(url: ""));
        var catalog = engine.Services.GetRequiredService<IModuleCatalog>();
        var before = catalog.FindModule("demo")!.Definition!.Version;

        File.AppendAllText(Path.Combine(_root, "demo", "module.yaml"), "\n# edited on the volume\n");
        await engine.Services.GetRequiredService<ModuleCatalogService>().RefreshAllAsync(TestContext.Current.CancellationToken);

        var module = catalog.FindModule("demo")!;
        Assert.Equal(ModuleStatus.Available, module.Status);
        Assert.NotEqual(before, module.Definition!.Version);
    }

    [Fact]
    public async Task ModuleWithoutFolderOnVolume_FallsBackToTheModuleService()
    {
        Directory.CreateDirectory(_root); // the volume is mounted but holds no folder for "demo"

        await using var engine = await TestEngine.StartAsync(configuration: VolumeConfig(url: "http://demo"));
        var catalog = engine.Services.GetRequiredService<IModuleCatalog>();
        Assert.Equal(ModuleStatus.Available, catalog.FindModule("demo")!.Status);

        engine.Modules.DownModules.Add("demo");
        await engine.Services.GetRequiredService<ModuleCatalogService>().RefreshAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ModuleStatus.Unavailable, catalog.FindModule("demo")!.Status);
    }

    private Dictionary<string, string?> VolumeConfig(string url) => new()
    {
        ["Flowgent:Definitions:Path"] = _root,
        ["Flowgent:Modules:0:Url"] = url,
    };

    private void WriteModule(string moduleId)
    {
        foreach (var (relativePath, content) in TestModules.Package(moduleId: moduleId).Files)
        {
            var target = Path.Combine(_root, moduleId, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
    }
}
