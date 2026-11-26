namespace Flowgent.Definitions.Yaml;

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

/// <summary>Result of reading a YAML document as JSON.</summary>
public sealed record YamlReadResult(JsonNode? Root, LineMap Lines, string? Error, int? ErrorLine)
{
    public bool IsSuccess => Error is null;
}

/// <summary>
/// Converts a YAML document into a <see cref="JsonNode"/>, keeping the line of every node (by JSON pointer)
/// so that validation errors can point to the YAML source. Plain scalars are typed (null, bool, number);
/// quoted and block scalars are always strings.
/// </summary>
public static partial class YamlJsonReader
{
    public static YamlReadResult Read(string yaml)
    {
        var lines = new LineMap();
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));

            if (stream.Documents.Count == 0)
            {
                return new(null, lines, "The document is empty.", 1);
            }

            if (stream.Documents.Count > 1)
            {
                return new(null, lines, "Only one YAML document per file is supported.", (int)stream.Documents[1].RootNode.Start.Line);
            }

            var root = Convert(stream.Documents[0].RootNode, "", lines, depth: 0);
            return new(root, lines, null, null);
        }
        catch (YamlException ex)
        {
            return new(null, lines, $"Invalid YAML: {ex.Message}", ex.Start.Line > 0 ? (int)ex.Start.Line : null);
        }
        catch (YamlDepthException ex)
        {
            return new(null, lines, ex.Message, null);
        }
        catch (InvalidOperationException)
        {
            // YamlDotNet reports some malformed documents (e.g. unclosed flow sequences) this way.
            return new(null, lines, "Invalid YAML: the document is malformed (check brackets, quotes and indentation).", null);
        }
    }

    private static JsonNode? Convert(YamlNode node, string pointer, LineMap lines, int depth)
    {
        if (depth > 64)
        {
            throw new YamlDepthException();
        }

        lines.Add(pointer, (int)node.Start.Line);

        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    var key = keyNode is YamlScalarNode scalarKey ? scalarKey.Value ?? "" : throw new YamlException(keyNode.Start, keyNode.End, "Mapping keys must be scalars.");
                    if (obj.ContainsKey(key))
                    {
                        throw new YamlException(keyNode.Start, keyNode.End, $"Duplicate key '{key}'.");
                    }

                    obj[key] = Convert(valueNode, $"{pointer}/{Escape(key)}", lines, depth + 1);
                }

                return obj;

            case YamlSequenceNode sequence:
                var array = new JsonArray();
                var index = 0;
                foreach (var item in sequence.Children)
                {
                    array.Add(Convert(item, $"{pointer}/{index++}", lines, depth + 1));
                }

                return array;

            case YamlScalarNode scalar:
                return ConvertScalar(scalar);

            default:
                throw new YamlException(node.Start, node.End, $"Unsupported YAML node '{node.NodeType}'.");
        }
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";

        if (scalar.Style is not ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        if (value is "" or "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (value is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }

        if (value is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }

        if (IntegerPattern().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (FloatPattern().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    private static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");

    [GeneratedRegex(@"^[-+]?[0-9]+$")]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex FloatPattern();
}

/// <summary>Line numbers of YAML nodes indexed by JSON pointer (e.g. <c>/steps/2/prompt</c>).</summary>
public sealed class LineMap
{
    private readonly Dictionary<string, int> _lines = new(StringComparer.Ordinal);

    internal void Add(string pointer, int line) => _lines[pointer] = line;

    /// <summary>Line of the node at <paramref name="pointer"/>, or of its closest known ancestor.</summary>
    public int? Find(string? pointer)
    {
        var current = pointer ?? "";
        while (true)
        {
            if (_lines.TryGetValue(current, out var line))
            {
                return line;
            }

            if (current.Length == 0)
            {
                return null;
            }

            var slash = current.LastIndexOf('/');
            current = slash <= 0 ? "" : current[..slash];
        }
    }
}

internal sealed class YamlDepthException() : Exception("The YAML document is nested too deeply.");
