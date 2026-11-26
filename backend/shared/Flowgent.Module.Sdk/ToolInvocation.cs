namespace Flowgent.Module.Sdk;

using System.Text.Json.Nodes;

/// <summary>Context of a tool call made by a workflow step.</summary>
public sealed record ToolInvocation(Guid RunId, string ModuleId, string WorkflowId, string StepId, JsonNode? Input, ModuleDefinitionProvider Module);
