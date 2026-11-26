namespace Flowgent.Engine.Service.Endpoints;

using Flowgent.Contracts.Api;
using Flowgent.Definitions.Schemas;
using Flowgent.Engine.Service.Endpoints.Mapping;
using Flowgent.Engine.Service.Endpoints.Primitives;
using Flowgent.Engine.Core.Configuration;
using Flowgent.Engine.Core.Llm;
using Flowgent.Engine.Core.Modules;
using Microsoft.Extensions.Options;

/// <summary>Runtime configuration, modules, workflows and the module-envelope JSON Schemas.</summary>
internal sealed class CatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EngineRoutes.Base).WithTags("Modules and workflows");

        group.MapGet("/config", GetConfig)
            .WithSummary("Runtime configuration for the dashboard (never secrets)");

        group.MapGet("/modules", GetModules)
            .WithSummary("Modules with availability and validation errors");

        group.MapGet("/workflows", GetWorkflows)
            .WithSummary("Workflows with graph, input schema and samples");

        group.MapGet("/workflows/{workflowId}", GetWorkflow)
            .WithSummary("A workflow definition");

        group.MapGet("/schemas/{name}", GetSchema)
            .WithSummary("JSON Schemas of the module envelope formats (module, samples)");
    }

    private static IResult GetConfig(IOptions<EngineOptions> engine, IOptions<LlmOptions> llm) =>
        Results.Ok(RuntimeConfigMapper.BuildConfig(engine.Value, llm.Value));

    private static IResult GetModules(IModuleCatalog catalog) =>
        Results.Ok(catalog.Modules.Select(m => m.ToInfo()));

    private static IResult GetWorkflows(IModuleCatalog catalog, string? moduleId) =>
        Results.Ok(catalog.Modules
            .Where(m => m.Definition is not null && (moduleId is null || m.Id == moduleId))
            .SelectMany(m => m.Definition!.Workflows.Select(w => WorkflowMapper.ToWorkflowInfo(m, w))));

    private static IResult GetWorkflow(string workflowId, IModuleCatalog catalog) =>
        catalog.FindWorkflow(workflowId) is { } handle
            ? Results.Ok(WorkflowMapper.ToWorkflowInfo(handle.Module, handle.Workflow))
            : ApiResults.NotFound($"Workflow '{workflowId}' not found.");

    private static IResult GetSchema(string name) =>
        FormatSchemas.GetText(name) is { } text
            ? Results.Text(text, "application/schema+json")
            : ApiResults.NotFound($"Schema '{name}' not found. Available: {string.Join(", ", FormatSchemas.Names)}.");
}
