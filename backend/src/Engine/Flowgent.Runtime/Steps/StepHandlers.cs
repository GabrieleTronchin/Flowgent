namespace Flowgent.Runtime.Steps;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flowgent.Definitions.Expressions;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Execution;

/// <summary>Executes one step kind and returns the step output (stored as <c>steps.&lt;id&gt;</c>).</summary>
public interface IStepHandler
{
    string Kind { get; }

    /// <summary>Steps doing I/O record a StepStarted event so the dashboard can show them in progress.</summary>
    bool RecordsStart { get; }

    Task<JsonNode?> ExecuteAsync(StepDefinition step, RunContext run, CancellationToken ct);
}

/// <summary>
/// Ordered rule matching for the custom-format step handlers (<see cref="RuleStepHandler"/> and llm
/// <c>demo.rules</c>), preserved verbatim (incl. the Scriban <c>When</c> branch) until those handlers
/// are deleted in a later feature. The retained, regex-only replacement that survives that deletion is
/// <see cref="Flowgent.Runtime.Matching.RuleMatcher"/> (works against <c>MatchRule</c>, a different
/// namespace). This one stays in <c>Flowgent.Runtime.Steps</c> so existing tests compile unchanged.
/// </summary>
public static class RuleMatcher
{
    private static readonly ConcurrentDictionary<(string, bool), Regex> Regexes = new();

    /// <summary>Indexes of the rules matching <paramref name="value"/>, in declaration order.</summary>
    public static IReadOnlyList<int> Match(IReadOnlyList<RuleDefinition> rules, string value, bool ignoreCase, EvaluationScope scope, TemplateEngine templates)
    {
        var matches = new List<int>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var matched = rule.Match is not null
                ? GetRegex(rule.Match, ignoreCase).IsMatch(value)
                : rule.When is not null && templates.EvaluateCondition(rule.When, scope);
            if (matched)
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

internal sealed class RuleStepHandler : IStepHandler
{
    public string Kind => StepKinds.Rule;

    public bool RecordsStart => false;

    public Task<JsonNode?> ExecuteAsync(StepDefinition step, RunContext run, CancellationToken ct)
    {
        var rule = (RuleStep)step;
        var scope = run.CreateScope();
        var templates = run.Templates;
        var value = templates.Render(rule.Value, scope);
        var matches = RuleMatcher.Match(rule.Rules, value, rule.IgnoreCase, scope, templates);

        if (matches.Count == 0)
        {
            return rule.OnNoMatch switch
            {
                { Fail: { } fail } => throw new StepFailedException(step.Id, templates.Render(fail, scope)),
                { } fallback => Task.FromResult(templates.EvaluateValue(fallback.Output, scope)),
                null => throw new StepFailedException(step.Id, $"No rule matched in step '{step.Id}'."),
            };
        }

        if (matches.Count > 1 && rule.OnMultipleMatches?.Fail is { } multiple)
        {
            throw new StepFailedException(step.Id, templates.Render(multiple, scope));
        }

        return Task.FromResult(templates.EvaluateValue(rule.Rules[matches[0]].Output, scope));
    }
}

internal sealed class MapStepHandler : IStepHandler
{
    public string Kind => StepKinds.Map;

    public bool RecordsStart => false;

    public Task<JsonNode?> ExecuteAsync(StepDefinition step, RunContext run, CancellationToken ct)
    {
        var map = (MapStep)step;
        var templates = run.Templates;
        var baseScope = run.CreateScope();
        var output = new JsonObject();

        foreach (var item in map.Outputs)
        {
            var scope = baseScope.With("output", output);
            if (item.Lookup is not null)
            {
                var key = templates.Render(item.Key ?? "", scope);
                var table = map.Lookups![item.Lookup];
                if (!table.TryGetPropertyValue(key, out var value))
                {
                    var message = item.OnMissing is not null
                        ? templates.Render(item.OnMissing, scope)
                        : $"No entry '{key}' in lookup '{item.Lookup}'.";
                    throw new StepFailedException(step.Id, message);
                }

                output[item.Name] = value?.DeepClone();
            }
            else
            {
                output[item.Name] = templates.EvaluateValue(item.Value, scope);
            }
        }

        return Task.FromResult<JsonNode?>(output);
    }
}
