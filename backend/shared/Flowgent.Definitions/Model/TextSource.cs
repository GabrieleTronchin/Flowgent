namespace Flowgent.Definitions.Model;

using System.Text.Json.Serialization;

/// <summary>Inline text (a template) or a reference to a file in the module definition folder.</summary>
[JsonConverter(typeof(TextSourceConverter))]
public sealed record TextSource(string? Inline, string? File)
{
    public static TextSource FromInline(string text) => new(text, null);

    public static TextSource FromFile(string path) => new(null, path);
}
