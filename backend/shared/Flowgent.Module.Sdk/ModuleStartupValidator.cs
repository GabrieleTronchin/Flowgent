namespace Flowgent.Module.Sdk;

using Flowgent.Contracts.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
