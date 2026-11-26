namespace Flowgent.Definitions.Model;

using System.Text.Json.Nodes;

public sealed class SampleDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Group { get; init; }

    public string? Description { get; init; }

    public required string Workflow { get; init; }

    public JsonObject? Input { get; init; }

    public bool ExpectInvalid { get; init; }
}
