namespace Flowgent.Runtime.Matching;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// A single regex rule and the output to select when it matches. Retained replacement for the
/// custom-format <c>RuleDefinition</c> (which is deleted with the step model in a later feature):
/// <see cref="RuleMatcher"/> works against this type, not the definition model.
/// </summary>
public sealed record MatchRule(string Pattern, JsonNode? Output);

/// <summary>
/// Ordered, regex-only rule matching. Retained after the <c>Steps/*</c> handlers are deleted;
/// used by the Demo response provider to pick a demo output. The Scriban <c>When</c> branch of the
/// old matcher is dropped (no module uses it); the regex cache and options are preserved verbatim.
/// </summary>
public static class RuleMatcher
{
    private static readonly ConcurrentDictionary<(string, bool), Regex> Regexes = new();

    /// <summary>Indexes of the rules whose pattern matches <paramref name="value"/>, in declaration order.</summary>
    public static IReadOnlyList<int> Match(IReadOnlyList<MatchRule> rules, string value, bool ignoreCase)
    {
        var matches = new List<int>();
        for (var i = 0; i < rules.Count; i++)
        {
            if (GetRegex(rules[i].Pattern, ignoreCase).IsMatch(value))
            {
                matches.Add(i);
            }
        }

        return matches;
    }

    private static Regex GetRegex(string pattern, bool ignoreCase) =>
        Regexes.GetOrAdd((pattern, ignoreCase), key => new Regex(
            key.Item1,
            RegexOptions.CultureInvariant | (key.Item2 ? RegexOptions.IgnoreCase : RegexOptions.None),
            TimeSpan.FromSeconds(1)));
}
