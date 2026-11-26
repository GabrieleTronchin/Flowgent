namespace Flowgent.Definitions.Model;

public sealed class ToolDefinition
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public SchemaRef? Input { get; init; }

    public SchemaRef? Output { get; init; }

    public int? TimeoutSeconds { get; init; }
}
