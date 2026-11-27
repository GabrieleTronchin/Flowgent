namespace Flowgent.Definitions;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Shared rules of the demo responses file: the regex options, the <c>{name}</c> placeholder syntax and how placeholders
/// are filled. Used by the loader (validation) and by the engine (Demo mode answers), so both agree.
/// </summary>
public static partial class DemoTemplate
{
    /// <summary>Placeholder always available: the whole trimmed rendered prompt.</summary>
    public const string PromptPlaceholder = "prompt";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> Cache = new();

    /// <summary>Regex of a rule <c>when</c>: matched against the prompt, case-insensitive.</summary>
    public static Regex When(string pattern) =>
        Get(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Regex of an extract <c>pattern</c>: case-sensitive, <c>.</c> also matches new lines.</summary>
    public static Regex Pattern(string pattern) =>
        Get(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>Placeholder names used in a text, e.g. <c>topic</c> for <c>"about {topic}"</c>.</summary>
    public static IEnumerable<string> Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value);

    /// <summary>Placeholder names used in every string of a JSON node.</summary>
    public static IEnumerable<string> Placeholders(JsonNode? node) => node switch
    {
        JsonObject obj => obj.SelectMany(p => Placeholders(p.Value)),
        JsonArray array => array.SelectMany(Placeholders),
        JsonValue value when value.TryGetValue<string>(out var text) => Placeholders(text),
        _ => [],
    };

    /// <summary>Replaces the known placeholders of a text; unknown ones are left as they are.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    /// <summary>A copy of <paramref name="node"/> with the placeholders of every string filled.</summary>
    public static JsonNode? Fill(JsonNode? node, IReadOnlyDictionary<string, string> values) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Fill(p.Value, values)))),
        JsonArray array => new JsonArray([.. array.Select(item => Fill(item, values))]),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(Fill(text, values)),
        _ => node?.DeepClone(),
    };

    private static Regex Get(string pattern, RegexOptions options) =>
        Cache.GetOrAdd((pattern, options), key => new Regex(key.Pattern, key.Options, RegexTimeout));

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
