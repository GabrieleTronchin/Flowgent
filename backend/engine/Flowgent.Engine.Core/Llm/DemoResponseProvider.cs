namespace Flowgent.Engine.Core.Llm;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Core.Runs.Execution;

/// <summary>
/// Computes the Demo-mode JSON answer of an agent step from the demo responses the module declares for the workflow
/// (<c>workflows[].demo</c> in <c>module.yaml</c>). The engine knows no workflow: a step without a declared response
/// fails with a message saying where to add it. The agent provider calls <see cref="Resolve"/> in Demo mode and
/// attaches the result with <see cref="LlmCallAttribution.WithDemoResponse"/> so <see cref="DemoChatClient"/> returns it.
/// </summary>
public sealed class DemoResponseProvider
{
    /// <summary>The demo answer of <paramref name="stepId"/> in the run's workflow, computed from the rendered prompt.</summary>
    public JsonNode Resolve(RunContext run, string stepId, string renderedPrompt) =>
        Resolve(run.Module.Id, run.Workflow, stepId, renderedPrompt);

    /// <summary>The demo answer of <paramref name="stepId"/> in <paramref name="workflow"/>, computed from the rendered prompt.</summary>
    public JsonNode Resolve(string moduleId, WorkflowDescriptor workflow, string stepId, string renderedPrompt)
    {
        if (workflow.Demo?.Steps.GetValueOrDefault(stepId) is not { } step)
        {
            var where = workflow.Demo is null
                ? $"Add a demo file to workflow '{workflow.Id}' (workflows[].demo in module.yaml of module '{moduleId}')"
                : $"Add step '{stepId}' to the demo file of workflow '{workflow.Id}' in module '{moduleId}'";
            throw new StepFailedException(stepId, $"No demo response is defined for step '{stepId}' of workflow '{workflow.Id}'. {where}, or use a real LLM provider.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal) { [DemoTemplate.PromptPlaceholder] = renderedPrompt.Trim() };
        foreach (var (name, extract) in step.Extract)
        {
            values[name] = Extract(extract, renderedPrompt, values);
        }

        var output = step.Rules.FirstOrDefault(r => DemoTemplate.When(r.When).IsMatch(renderedPrompt))?.Output ?? step.Output
            ?? throw new StepFailedException(stepId, $"No demo rule matches the prompt of step '{stepId}' of workflow '{workflow.Id}' and the step has no default output.");

        return DemoTemplate.Fill(output, values)!;
    }

    private static string Extract(DemoExtract extract, string prompt, IReadOnlyDictionary<string, string> values)
    {
        var value = extract.Field is { } label ? Field(prompt, label) : Match(extract.Pattern!, prompt);
        return value.Length > 0 || extract.Default is null ? value : DemoTemplate.Fill(extract.Default, values);
    }

    private static string Match(string pattern, string prompt)
    {
        var match = DemoTemplate.Pattern(pattern).Match(prompt);
        if (!match.Success)
        {
            return "";
        }

        return (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Trim();
    }

    /// <summary>
    /// Extracts a <c>Label: value</c> field from a rendered prompt. The value runs to the end of the line, or — for a
    /// trailing multi-line field (e.g. Body) — to the next blank line or the next <c>Label:</c> line.
    /// </summary>
    internal static string Field(string prompt, string label)
    {
        var match = Regex.Match(prompt, $@"(?:^|\n){Regex.Escape(label)}:[ \t]*(.*?)(?=\n\n|\n[A-Za-z][A-Za-z ]*:|$)", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}
