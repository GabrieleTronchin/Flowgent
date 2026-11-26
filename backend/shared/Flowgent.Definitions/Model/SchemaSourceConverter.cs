namespace Flowgent.Definitions.Model;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
