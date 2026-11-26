namespace Flowgent.Definitions.Model;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>Inline text (a template) or a reference to a file in the module definition folder.</summary>
[JsonConverter(typeof(TextSourceConverter))]
public sealed record TextSource(string? Inline, string? File)
{
    public static TextSource FromInline(string text) => new(text, null);

    public static TextSource FromFile(string path) => new(null, path);
}

/// <summary>Inline JSON Schema or a reference to a schema file (<c>{ file: path }</c>).</summary>
[JsonConverter(typeof(SchemaSourceConverter))]
public sealed record SchemaSource(JsonObject? Inline, string? File)
{
    /// <summary>Stable key used to cache the compiled schema.</summary>
    public string Key => File is not null ? $"file:{File}" : $"inline:{Inline!.ToJsonString()}";
}

/// <summary><c>{ schema: ... }</c> wrapper used by llm outputs, tool inputs/outputs and http outputs.</summary>
public sealed class SchemaRef
{
    public required SchemaSource Schema { get; init; }
}

internal sealed class TextSourceConverter : JsonConverter<TextSource>
{
    public override TextSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return TextSource.FromInline(reader.GetString()!);
        }

        var node = JsonNode.Parse(ref reader) as JsonObject;
        return node?["file"]?.GetValue<string>() is { } file
            ? TextSource.FromFile(file)
            : throw new JsonException("Expected a string or { file: path }.");
    }

    public override void Write(Utf8JsonWriter writer, TextSource value, JsonSerializerOptions options)
    {
        if (value.File is not null)
        {
            writer.WriteStartObject();
            writer.WriteString("file", value.File);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStringValue(value.Inline);
        }
    }
}

internal sealed class SchemaSourceConverter : JsonConverter<SchemaSource>
{
    public override SchemaSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject obj)
        {
            throw new JsonException("Expected an inline JSON Schema object or { file: path }.");
        }

        return obj.Count == 1 && obj["file"] is JsonValue fileValue && fileValue.TryGetValue<string>(out var file)
            ? new SchemaSource(null, file)
            : new SchemaSource(obj, null);
    }

    public override void Write(Utf8JsonWriter writer, SchemaSource value, JsonSerializerOptions options)
    {
        if (value.File is not null)
        {
            writer.WriteStartObject();
            writer.WriteString("file", value.File);
            writer.WriteEndObject();
        }
        else
        {
            value.Inline!.WriteTo(writer);
        }
    }
}
