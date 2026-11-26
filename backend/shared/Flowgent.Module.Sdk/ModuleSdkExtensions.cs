// RemoveAllResilienceHandlers is experimental: the engine client must not retry intake requests
// (a retried POST could create a duplicate run).
#pragma warning disable EXTEXP0001

namespace Flowgent.Module.Sdk;

using System.Text.Json;
using Flowgent.Contracts.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

public static class ModuleSdkExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Registers the Module SDK: definition provider (<c>Flowgent:Module:DefinitionPath</c>), startup validation,
    /// tool registry and <see cref="IEngineClient"/>.
    /// </summary>
    public static IHostApplicationBuilder AddFlowgentModule(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ModuleSdkOptions>(builder.Configuration.GetSection(ModuleSdkOptions.SectionName));
        builder.Services.AddSingleton<ModuleDefinitionProvider>();
        // Create (or reuse) the registry holder now, during configuration, so it works for modules without any tool.
        // Resolving it in a factory would run after the service collection is read-only.
        var registry = GetRegistry(builder.Services);
        builder.Services.AddSingleton(registry);
        builder.Services.AddHostedService<ModuleStartupValidator>();

        builder.Services.AddHttpClient<IEngineClient, EngineClient>(EngineClient.HttpClientName, (sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ModuleSdkOptions>>().Value.EngineUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).RemoveAllResilienceHandlers();

        return builder;
    }

    /// <summary>Registers a tool handler for a tool declared in <c>module.yaml</c>.</summary>
    public static IServiceCollection AddModuleTool<TTool>(this IServiceCollection services, string name)
        where TTool : class, IModuleTool
    {
        GetRegistry(services).Add(name, typeof(TTool));
        services.AddTransient<TTool>();
        return services;
    }

    /// <summary>
    /// Maps the SDK endpoints: <c>GET /_module/package</c>, <c>POST /_module/tools/{name}</c> and <c>GET /api/modules/{id}</c>.
    /// These endpoints are meant for the private network (engine → module); only <c>/api/modules/**</c> is routed by the gateway.
    /// </summary>
    public static IEndpointRouteBuilder MapFlowgentModule(this IEndpointRouteBuilder app)
    {
        var provider = app.ServiceProvider.GetRequiredService<ModuleDefinitionProvider>();
        var internalApi = app.MapGroup("/_module").WithTags("Module SDK");

        internalApi.MapGet("/package", (HttpContext context, ModuleDefinitionProvider definitions) =>
        {
            var package = definitions.Package;
            var etag = $"\"{package.Version}\"";
            context.Response.Headers.ETag = etag;
            if (context.Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Json(package, JsonOptions);
        }).WithSummary("Module definition files and version (pulled by the engine)");

        internalApi.MapPost("/tools/{name}", async (string name, ToolInvocationRequest request, ModuleToolRegistry registry, IServiceProvider services, ModuleDefinitionProvider definitions, CancellationToken ct) =>
        {
            var tool = registry.Resolve(services, name);
            if (tool is null)
            {
                return Results.Json(new ToolInvocationResponse(null, $"Unknown tool '{name}'."), JsonOptions, statusCode: StatusCodes.Status404NotFound);
            }

            try
            {
                var output = await tool.InvokeAsync(new ToolInvocation(request.RunId, request.ModuleId, request.WorkflowId, request.StepId, request.Input, definitions), ct);
                return Results.Json(new ToolInvocationResponse(output), JsonOptions);
            }
            catch (ToolException ex)
            {
                return Results.Json(new ToolInvocationResponse(null, ex.Message), JsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new ToolInvocationResponse(null, ex.Message), JsonOptions, statusCode: StatusCodes.Status500InternalServerError);
            }
        }).WithSummary("Invoke a module tool (called by the engine)");

        app.MapGet($"/api/modules/{provider.ModuleId}", (ModuleDefinitionProvider definitions) =>
        {
            var module = definitions.LoadResult.Module;
            return Results.Ok(new
            {
                id = definitions.ModuleId,
                name = module?.Manifest.Name,
                description = module?.Manifest.Description,
                version = definitions.Package.Version,
                valid = definitions.LoadResult.IsValid,
                workflows = module?.Workflows.Select(w => w.Id).ToList() ?? [],
            });
        }).WithTags("Module").WithSummary("Module summary");

        return app;
    }

    private static ModuleToolRegistry GetRegistry(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ModuleToolRegistryHolder));
        if (descriptor?.ImplementationInstance is ModuleToolRegistryHolder holder)
        {
            return holder.Registry;
        }

        var created = new ModuleToolRegistryHolder();
        services.AddSingleton(created);
        return created.Registry;
    }

    private sealed class ModuleToolRegistryHolder
    {
        public ModuleToolRegistry Registry { get; } = new();
    }
}

/// <summary>Assertions for module unit tests.</summary>
public static class ModuleDefinitionAssert
{
    /// <summary>Loads a definition folder and throws with every error when it is invalid.</summary>
    public static Definitions.ModuleDefinition IsValid(string definitionPath)
    {
        var result = Definitions.ModuleLoader.LoadFromDirectory(definitionPath);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("The module definition is invalid:" + Environment.NewLine +
                string.Join(Environment.NewLine, result.BlockingErrors.Select(e => $"  {e.File}:{e.Line} [{e.StepId}] {e.Message}")));
        }

        return result.Module!;
    }
}
