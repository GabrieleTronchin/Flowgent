namespace Flowgent.Definitions.Model;
/// <summary><c>{ schema: ... }</c> wrapper used by llm outputs, tool inputs/outputs and http outputs.</summary>
public sealed class SchemaRef
{
    public required SchemaSource Schema { get; init; }
}
