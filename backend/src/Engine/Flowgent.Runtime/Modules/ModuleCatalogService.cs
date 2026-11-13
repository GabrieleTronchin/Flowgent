namespace Flowgent.Runtime.Modules;

using Flowgent.Runtime.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Pulls module packages in the background: at startup, then every <see cref="ModuleEndpointOptions.RefreshSeconds"/>.
/// Unavailable modules are retried on the same interval; the engine never waits for modules to start.
/// </summary>
public sealed class ModuleCatalogService(
    ModuleCatalog catalog,
    VolumeDefinitionSource volume,
    HttpDefinitionSource http,
    IOptions<EngineOptions> options,
    ILogger<ModuleCatalogService> logger) : BackgroundService
{
    /// <summary>Checks every module once (also used by tests to load modules deterministically).</summary>
    public async Task RefreshAllAsync(CancellationToken ct = default) =>
        await Task.WhenAll(catalog.Modules.Select(m => RefreshAsync(m.Id, ct)));

    public async Task RefreshAsync(string moduleId, CancellationToken ct = default)
    {
        var current = catalog.FindModule(moduleId);
        if (current is null)
        {
            return;
        }

        // Volume is primary when the module's folder exists on the mounted path; otherwise pull over HTTP.
        var source = volume.HasModule(moduleId) ? (IDefinitionSource)volume : http;
        var result = await source.FetchAsync(moduleId, current.Definition?.Version, ct);
        switch (result)
        {
            case PackageFetchResult.Changed changed:
                catalog.ApplyPackage(moduleId, changed.Package);
                break;
            case PackageFetchResult.NotModified:
                catalog.ApplyNotModified(moduleId);
                break;
            case PackageFetchResult.Failed failed:
                catalog.ApplyFailure(moduleId, failed.Error);
                break;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var modules = options.Value.Modules.Where(m => m.Enabled).GroupBy(m => m.Id).Select(g => g.First()).ToList();
        logger.LogInformation("Module catalog started for {Count} module(s): {Modules}", modules.Count, string.Join(", ", modules.Select(m => m.Id)));
        return Task.WhenAll(modules.Select(m => PollAsync(m, stoppingToken)));
    }

    private async Task PollAsync(ModuleEndpointOptions module, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0.05, module.RefreshSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(module.Id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Refreshing module {ModuleId} failed", module.Id);
                catalog.ApplyFailure(module.Id, ex.Message);
            }

            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
