namespace Flowgent.Contracts.Modules;

using System.Text.Json.Nodes;

/// <summary>Routes every module service exposes through the Module SDK.</summary>
public static class ModuleRoutes
{
    public const string Package = "/_module/package";
    public const string Tools = "/_module/tools";

    public static string Tool(string name) => $"{Tools}/{Uri.EscapeDataString(name)}";
}

/// <summary>
/// All files of a module definition folder (paths relative to <c>definition/</c>, '/' separated) and the
/// version of the package (SHA-256 of the files). Served by <c>GET /_module/package</c>.
/// </summary>
public sealed record ModulePackage(string ModuleId, string Version, IReadOnlyDictionary<string, string> Files);

/// <summary>Body of <c>POST /_module/tools/{name}</c>.</summary>
public sealed record ToolInvocationRequest(Guid RunId, string ModuleId, string WorkflowId, string StepId, JsonNode? Input);

/// <summary>Response of a tool invocation: <see cref="Output"/> on success, <see cref="Error"/> otherwise.</summary>
public sealed record ToolInvocationResponse(JsonNode? Output, string? Error = null);
