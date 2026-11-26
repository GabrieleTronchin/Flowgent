namespace Flowgent.Engine.Core.Llm;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

/// <summary>
/// Finds which ordered regex rules match a value. Used by the Demo response provider to choose a canned answer.
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
