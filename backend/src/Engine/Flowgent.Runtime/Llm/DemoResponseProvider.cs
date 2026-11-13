namespace Flowgent.Runtime.Llm;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Matching;

/// <summary>
/// Computes the Demo-mode JSON response for an <c>llm</c> step, keyed by <c>(workflowId, stepId)</c> (design §7).
/// Pure and unit-testable: the demo behaviour that used to live in each workflow's <c>demo:</c> block (Scriban) is
/// relocated here in C#, so the migrated declarative YAML carries no <c>demo:</c>. The agent provider calls
/// <see cref="Resolve"/> in Demo mode and attaches the result with <see cref="LlmCallAttribution.WithDemoResponse"/>
/// so <see cref="DemoChatClient"/> returns it verbatim.
/// </summary>
public sealed partial class DemoResponseProvider
{
    // support.classify rules, ported verbatim from support-email.yaml demo.rules (regex + output), matched in order.
    private static readonly IReadOnlyList<MatchRule> ClassifyRules =
    [
        new MatchRule(
            @"\btests?\b|flaky|coverage",
            new JsonObject { ["isCodeRelated"] = true, ["category"] = "QualityTestIssue", ["confidenceScore"] = 0.88, ["reasoning"] = "[Demo] Mentions tests, coverage or flakiness." }),
        new MatchRule(
            @"frontend|front-end|\.razor|component|\bcss\b|\bui\b|button|render|display",
            new JsonObject { ["isCodeRelated"] = true, ["category"] = "FrontendBug", ["confidenceScore"] = 0.9, ["reasoning"] = "[Demo] Mentions UI components or rendering." }),
        new MatchRule(
            @"exception|error|crash|\bnull\b|\bsql\b|\bapi\b|endpoint|database|\bbug\b|latency|controller|injection|timeout",
            new JsonObject { ["isCodeRelated"] = true, ["category"] = "BackendBug", ["confidenceScore"] = 0.92, ["reasoning"] = "[Demo] Mentions exceptions, APIs or data access." }),
    ];

    private static readonly JsonObject ClassifyDefault = new()
    {
        ["isCodeRelated"] = false,
        ["category"] = "OutOfScope",
        ["confidenceScore"] = 0.95,
        ["reasoning"] = "[Demo] No code-related signal found; looks like a general support request.",
    };

    /// <summary>
    /// Returns the demo output JSON for the given step. <paramref name="renderedPrompt"/> is the user prompt the agent
    /// would receive (used for topic extraction and rule matching); <paramref name="run"/> provides cross-step outputs
    /// (<c>steps.*</c>) and the original input.
    /// </summary>
    public JsonNode Resolve(string workflowId, string stepId, string renderedPrompt, RunContext run) =>
        (workflowId, stepId) switch
        {
            ("joke", "tell") => JokeTell(renderedPrompt),
            ("support-email", "classify") => Classify(renderedPrompt),
            ("support-email", "resolve") => Resolve(renderedPrompt),
            ("support-email", "generate_code") => GenerateCode(renderedPrompt),
            ("template-hello", "summarize") => Summarize(renderedPrompt),
            ("fixture-flow", "draft") => FixtureDraft(renderedPrompt),
            ("hello", "summarize") => HelloSummarize(renderedPrompt),
            ("review", "write") => ReviewWrite(renderedPrompt),
            _ => throw new StepFailedException(stepId, $"No demo response is defined for step '{stepId}' of workflow '{workflowId}'."),
        };

    // Engine.Tests TestModules.hello: demo summary "[Demo] <text>" from prompt "Summarise <text>".
    private static JsonObject HelloSummarize(string renderedPrompt)
    {
        var match = Regex.Match(renderedPrompt, @"Summarise\s*(.+)", RegexOptions.Singleline);
        var text = match.Success ? match.Groups[1].Value.Trim() : renderedPrompt.Trim();
        return new JsonObject { ["summary"] = $"[Demo] {text}" };
    }

    // Engine.Tests TestModules.review: demo write rule — "urgent" -> "URGENT <team>", else "normal <team>".
    // The migrated prompt renders "Write for <team>" (Subject carried for the urgent keyword).
    private static JsonObject ReviewWrite(string renderedPrompt)
    {
        var team = Field(renderedPrompt, "Team");
        if (string.IsNullOrEmpty(team))
        {
            var forMatch = Regex.Match(renderedPrompt, @"Write for\s*(\S+)", RegexOptions.Singleline);
            team = forMatch.Success ? forMatch.Groups[1].Value.Trim() : "";
        }

        var urgent = renderedPrompt.Contains("urgent", StringComparison.OrdinalIgnoreCase);
        return new JsonObject { ["summary"] = urgent ? $"URGENT {team}" : $"normal {team}" };
    }

    // Test-only fixtures module (backend/tests/Fixtures): the pre-migration demo output was { text: "draft <text>" };
    // the migrated prompt renders "Write a draft for: <text>".
    private static JsonObject FixtureDraft(string renderedPrompt)
    {
        var match = Regex.Match(renderedPrompt, @"Write a draft for:\s*(.+)", RegexOptions.Singleline);
        var text = match.Success ? match.Groups[1].Value.Trim() : renderedPrompt.Trim();
        return new JsonObject { ["text"] = $"draft {text}" };
    }

    private static JsonObject JokeTell(string renderedPrompt)
    {
        var topic = ExtractTopic(renderedPrompt);
        return new JsonObject { ["joke"] = $"[Demo] Why did the {topic} cross the road? To get to the other side." };
    }

    private static JsonObject Classify(string renderedPrompt)
    {
        var matches = RuleMatcher.Match(ClassifyRules, renderedPrompt, ignoreCase: true);
        var selected = matches.Count > 0 ? ClassifyRules[matches[0]].Output : ClassifyDefault;
        return (JsonObject)(selected?.DeepClone() ?? ClassifyDefault.DeepClone());
    }

    // The resolve/generate_code demos read their inputs from the rendered prompt (not RunContext) so they do not depend
    // on when the seam surfaces prior-step outputs from run state. The migrated resolve prompt renders
    // "Subject: ..\n\nBody: ..\n\nComponent: .." and generate_code renders "Component: ..\n\nProposedFix: ..\n\nPath: ..".
    private static JsonObject Resolve(string renderedPrompt)
    {
        var subject = Field(renderedPrompt, "Subject");
        var body = Field(renderedPrompt, "Body");
        var component = Field(renderedPrompt, "Component");
        var severity = AssessSeverity($"{subject} {body}");
        return new JsonObject
        {
            ["rootCauseDescription"] = $"[Demo] Simulated root cause analysis for \"{subject}\": the defect originates in {component}.",
            ["affectedComponent"] = component,
            ["severityAssessment"] = severity,
            ["proposedFixSummary"] = $"[Demo] Apply a targeted fix in {component} and add a regression test.",
            ["requiresEscalation"] = false,
            ["escalationReason"] = null,
        };
    }

    private static JsonObject GenerateCode(string renderedPrompt)
    {
        var component = Field(renderedPrompt, "Component");
        var proposedFix = Field(renderedPrompt, "ProposedFix");
        var path = Field(renderedPrompt, "Path");
        return new JsonObject
        {
            ["title"] = $"[Demo] Fix {component}",
            ["description"] = proposedFix,
            ["affectedFilePaths"] = new JsonArray(path),
            ["simulatedDiff"] =
                $"--- a/{path}\n+++ b/{path}\n@@ -1,3 +1,4 @@\n+// [Demo] simulated change: {proposedFix}\n // existing code\n",
        };
    }

    private static JsonObject Summarize(string renderedPrompt)
    {
        var text = Field(renderedPrompt, "Text");
        return new JsonObject { ["summary"] = $"[Demo] {Truncate(text, 80)}" };
    }

    /// <summary>
    /// Extracts a <c>Label: value</c> field from a rendered prompt. The value runs to the end of the line, or — for a
    /// trailing multi-line field (e.g. Body) — to the next blank line, matching how the prompts are rendered.
    /// </summary>
    internal static string Field(string prompt, string label)
    {
        var match = Regex.Match(prompt, $@"(?:^|\n){Regex.Escape(label)}:[ \t]*(.*?)(?=\n\n|\n[A-Za-z][A-Za-z ]*:|$)", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    /// <summary>
    /// Ports the Scriban <c>severityAssessment</c> expression: the lower-cased subject+body, checked for keywords.
    /// injection/security → Critical; exception/crash → High; otherwise Medium.
    /// </summary>
    internal static string AssessSeverity(string text)
    {
        var lowered = text.ToLowerInvariant();
        if (lowered.Contains("injection", StringComparison.Ordinal) || lowered.Contains("security", StringComparison.Ordinal))
        {
            return "Critical";
        }

        return lowered.Contains("exception", StringComparison.Ordinal) || lowered.Contains("crash", StringComparison.Ordinal)
            ? "High"
            : "Medium";
    }

    /// <summary>
    /// Pulls the joke topic out of the rendered prompt. The migrated jokes prompt renders as
    /// <c>Tell a joke about: &lt;topic&gt;</c>; if that marker is absent the whole trimmed prompt is used.
    /// </summary>
    internal static string ExtractTopic(string renderedPrompt)
    {
        var match = TopicMarker().Match(renderedPrompt);
        if (match.Success)
        {
            var captured = match.Groups[1].Value.Trim();
            if (captured.Length > 0)
            {
                return captured;
            }
        }

        return renderedPrompt.Trim();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"[Tt]ell a joke about:\s*(.+)", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TopicMarker();
}
