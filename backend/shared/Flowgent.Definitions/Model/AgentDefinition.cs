namespace Flowgent.Definitions.Model;

public sealed class AgentDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Group { get; init; }

    public string? Role { get; init; }

    public string? Persona { get; init; }
}
