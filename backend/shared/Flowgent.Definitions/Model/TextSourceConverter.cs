namespace Flowgent.Definitions.Model;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
