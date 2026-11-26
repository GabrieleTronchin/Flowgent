namespace Flowgent.Definitions.Model;

using System.Text.Json.Nodes;

/// <summary><c>module.yaml</c>.</summary>
public sealed class ModuleManifest
{
    public const string FileName = "module.yaml";

    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public JsonObject? Config { get; init; }

    public List<AgentDefinition> Agents { get; init; } = [];

    public List<ToolDefinition> Tools { get; init; } = [];

    public List<WorkflowRef> Workflows { get; init; } = [];

    public List<string> Samples { get; init; } = [];
}
