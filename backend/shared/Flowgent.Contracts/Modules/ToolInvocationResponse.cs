namespace Flowgent.Contracts.Modules;

using System.Text.Json.Nodes;

/// <summary>Response of a tool invocation: <see cref="Output"/> on success, <see cref="Error"/> otherwise.</summary>
public sealed record ToolInvocationResponse(JsonNode? Output, string? Error = null);
