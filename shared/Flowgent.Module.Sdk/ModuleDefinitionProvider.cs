namespace Flowgent.Module.Sdk;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Module SDK configuration (section <c>Flowgent:Module</c>).</summary>
public sealed class ModuleSdkOptions
{
    public const string SectionName = "Flowgent:Module";

    /// <summary>Definition folder, absolute or relative to the content root (default: <c>../definition</c>).</summary>
    public string DefinitionPath { get; set; } = "../definition";

    /// <summary>Stop the service when its definition is invalid. Default false, so the engine can show the errors.</summary>
    public bool FailOnInvalidDefinition { get; set; }

    /// <summary>Engine base address for <see cref="IEngineClient"/> (service discovery name under Aspire).</summary>
    public string EngineUrl { get; set; } = "http://localhost:5081";
}

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

/// <summary>Validates the definition at startup and checks that every declared tool has a handler.</summary>
internal sealed class ModuleStartupValidator(
    ModuleDefinitionProvider provider,
    ModuleToolRegistry tools,
    IOptions<ModuleSdkOptions> options,
    ILogger<ModuleStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var result = provider.LoadResult;
        foreach (var error in result.Errors)
        {
            var level = error.Severity == DefinitionSeverity.Error ? LogLevel.Error : LogLevel.Warning;
            logger.Log(level, "Module definition {Severity}: {File}:{Line} [{Step}] {Message}", error.Severity, error.File, error.Line, error.StepId, error.Message);
        }

        var missing = result.Module?.Manifest.Tools.Select(t => t.Name).Where(name => !tools.Contains(name)).ToList() ?? [];
        foreach (var name in missing)
        {
            logger.LogError("Tool {Tool} is declared in module.yaml but no handler is registered (AddModuleTool).", name);
        }

        if (options.Value.FailOnInvalidDefinition && (!result.IsValid || missing.Count > 0))
        {
            throw new InvalidOperationException($"The definition of module '{provider.ModuleId}' is invalid; see the log for details.");
        }

        logger.LogInformation("Module {ModuleId} definition {State} (version {Version}, folder {Folder})",
            provider.ModuleId, result.IsValid ? "is valid" : "is INVALID", provider.Package.Version, provider.DefinitionRoot);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
