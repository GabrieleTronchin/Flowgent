// RemoveAllResilienceHandlers is experimental: it removes the service-wide standard handler (ServiceDefaults),
// whose 10 s attempt timeout would cut long tool and HTTP step calls. Timeouts are applied per call instead.
#pragma warning disable EXTEXP0001

namespace Flowgent.Engine.Core;

using Flowgent.Definitions;
using Flowgent.Engine.Core.Configuration;
using Flowgent.Engine.Core.Llm;
using Flowgent.Engine.Core.Maf.Building;
using Flowgent.Engine.Core.Maf.Execution;
using Flowgent.Engine.Core.Maf.Mcp;
using Flowgent.Engine.Core.Modules.Catalog;
using Flowgent.Engine.Core.Modules.Client;
using Flowgent.Engine.Core.Modules.Sources;
using Flowgent.Engine.Core.Modules.Tools;
using Flowgent.Engine.Core.Runs.Execution;
using Flowgent.Engine.Core.Runs.HumanInput;
using Flowgent.Engine.Core.Runs.Intake;
using Flowgent.Engine.Core.Runs.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;

public static class RuntimeServiceExtensions
{
    /// <summary>
    /// Registers the engine runtime: module catalog, the MAF declarative seam, orchestrator, human input, intake,
    /// inbox processor, LLM client and telemetry. Persistence (<see cref="IRunStore"/>, <see cref="IInboxStore"/>) is registered separately.
    /// </summary>
    public static IServiceCollection AddFlowgentRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EngineOptions.SectionName);
        services.Configure<EngineOptions>(section);
        var engineOptions = section.Get<EngineOptions>() ?? new EngineOptions();

        services.AddFlowgentChatClient(configuration);

        services.AddSingleton<ModuleCatalog>();
        services.AddSingleton<IModuleCatalog>(sp => sp.GetRequiredService<ModuleCatalog>());
        services.TryAddSingleton<IModuleClient, ModuleClient>();
        services.AddSingleton<ModuleCatalogService>();
        services.AddHostedService(sp => sp.GetRequiredService<ModuleCatalogService>());

        services.AddSingleton<RunUpdateChannel>();
        services.AddSingleton<RunRecorder>();
        services.AddSingleton<RunRegistry>();
        services.AddSingleton<HumanInputRegistry>();
        services.AddSingleton<HumanInputService>();

        // MAF declarative seam: tool invoker, agent provider (per-run via the factory), MCP tool handler,
        // Demo response provider, conversation store, trial-build validator, graph projector, workflow factory and run
        // driver. The MAF path drives runs; the custom-format step handlers and compiler have been removed.
        services.AddSingleton<ModuleToolInvoker>();
        services.AddSingleton<DemoResponseProvider>();
        services.AddSingleton<IConversationStore, RunConversationStore>();
        services.AddTransient<Func<IConversationStore>>(sp => () => new RunConversationStore());
        services.AddSingleton<IWorkflowGraphProjector, WorkflowProjector>();
        services.AddSingleton<IDeclarativeDefinitionValidator, DeclarativeValidation>();
        services.AddSingleton<MafWorkflowFactory>();
        services.AddSingleton<MafRunDriver>();

        // MCP-over-HTTP. The pool caches one McpClient per server URL; the transport factory builds a
        // Streamable-HTTP transport over the named "mcp" IHttpClientFactory client (ownsHttpClient:false), with
        // per-call timeouts (so no client-level timeout) like the per-module clients.
        services.TryAddSingleton<IMcpTransportFactory, HttpMcpTransportFactory>();
        services.AddSingleton<McpClientPool>();
        services.AddHttpClient(HttpMcpTransportFactory.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllResilienceHandlers();

        services.AddSingleton<VolumeDefinitionSource>();
        services.AddSingleton<IDefinitionSource>(sp => sp.GetRequiredService<VolumeDefinitionSource>());
        services.AddSingleton<HttpDefinitionSource>();
        services.AddSingleton<IDefinitionSource>(sp => sp.GetRequiredService<HttpDefinitionSource>());

        services.AddSingleton<RunOrchestrator>();
        services.TryAddSingleton<IRunExecutor>(sp => sp.GetRequiredService<RunOrchestrator>());
        services.AddSingleton<IntakeService>();
        services.AddSingleton<InboxProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<InboxProcessor>());
        services.AddSingleton<AgentStatusService>();

        foreach (var module in engineOptions.Modules.Where(m => m.Enabled).GroupBy(m => m.Id).Select(g => g.First()))
        {
            AddModuleHttpClient(services, module);
        }

        return services;
    }

    /// <summary>
    /// One client per module with its own retry policy. Timeouts are applied per call (package fetch, tool),
    /// so the client itself has no timeout.
    /// </summary>
    private static void AddModuleHttpClient(IServiceCollection services, ModuleEndpointOptions module)
    {
        var builder = services.AddHttpClient(ModuleEndpointOptions.HttpClientName(module.Id), client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllResilienceHandlers();

        if (module.Retries > 0)
        {
            builder.AddResilienceHandler("flowgent-module", pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = module.Retries,
                Delay = TimeSpan.FromMilliseconds(Math.Max(1, module.RetryDelayMilliseconds)),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            }));
        }
    }
}
