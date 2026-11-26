namespace Flowgent.Contracts.Modules;

using System.Text.Json.Nodes;

/// <summary>Body of <c>POST /_module/tools/{name}</c>.</summary>
public sealed record ToolInvocationRequest(Guid RunId, string ModuleId, string WorkflowId, string StepId, JsonNode? Input);
