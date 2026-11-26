namespace Flowgent.Engine.Tests.Definitions;

using System.Text.Json.Nodes;
using Flowgent.Definitions.Yaml;

public class YamlJsonReaderTests
{
    [Fact]
    public void PlainScalars_AreTyped_QuotedScalarsStayStrings()
    {
        var result = YamlJsonReader.Read("""
            a: true
            b: 42
            c: 0.5
            d: ~
            e: "true"
            f: '42'
            g: text
            h: |
              line
            i: end
            """);

        Assert.True(result.IsSuccess);
        var root = result.Root!.AsObject();
        Assert.True(root["a"]!.GetValue<bool>());
        Assert.Equal(42, root["b"]!.GetValue<long>());
        Assert.Equal(0.5, root["c"]!.GetValue<double>());
        Assert.Null(root["d"]);
        Assert.True(root.ContainsKey("d"));
        Assert.Equal("true", root["e"]!.GetValue<string>());
        Assert.Equal("42", root["f"]!.GetValue<string>());
        Assert.Equal("text", root["g"]!.GetValue<string>());
        Assert.Equal("line\n", root["h"]!.GetValue<string>());
    }

    [Fact]
    public void Lines_AreTrackedByJsonPointer()
    {
        var result = YamlJsonReader.Read("""
            id: x
            steps:
              - id: one
                prompt: hi
              - id: two
            """);

        Assert.Equal(1, result.Lines.Find("/id"));
        Assert.Equal(4, result.Lines.Find("/steps/0/prompt"));
        Assert.Equal(5, result.Lines.Find("/steps/1"));
        // Unknown pointers fall back to the closest ancestor.
        Assert.Equal(5, result.Lines.Find("/steps/1/missing/deep"));
    }

    [Fact]
    public void SequencesAndNestedMappings_AreConverted()
    {
        var result = YamlJsonReader.Read("list: [1, two, { three: 3 }]");

        var list = Assert.IsType<JsonArray>(result.Root!["list"]);
        Assert.Equal(3, list.Count);
        Assert.Equal("two", list[1]!.GetValue<string>());
        Assert.Equal(3, list[2]!["three"]!.GetValue<long>());
    }

    [Fact]
    public void InvalidYaml_ReturnsErrorWithLine()
    {
        var result = YamlJsonReader.Read("a: 1\nb: 'unterminated\nc: 2");

        Assert.False(result.IsSuccess);
        Assert.True(result.Error!.StartsWith("Invalid YAML", StringComparison.Ordinal), result.Error);
        Assert.NotNull(result.ErrorLine);
    }

    [Fact]
    public void UnclosedFlowSequence_IsInvalidYaml()
    {
        var result = YamlJsonReader.Read("a: 1\nb: [unclosed\nc: 2");

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Invalid YAML", result.Error);
    }

    [Fact]
    public void DuplicateKeys_AreRejected()
    {
        var result = YamlJsonReader.Read("a: 1\na: 2");

        Assert.False(result.IsSuccess);
        Assert.Contains("Duplicate", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyDocument_IsAnError()
    {
        Assert.False(YamlJsonReader.Read("").IsSuccess);
    }
}
