namespace Flowgent.Definitions.Model;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>Inline JSON Schema or a reference to a schema file (<c>{ file: path }</c>).</summary>
[JsonConverter(typeof(SchemaSourceConverter))]
public sealed record SchemaSource(JsonObject? Inline, string? File)
{
    /// <summary>Stable key used to cache the compiled schema.</summary>
    public string Key => File is not null ? $"file:{File}" : $"inline:{Inline!.ToJsonString()}";
}
