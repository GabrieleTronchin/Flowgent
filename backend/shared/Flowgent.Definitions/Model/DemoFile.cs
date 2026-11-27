namespace Flowgent.Definitions.Model;

using System.Text.Json.Nodes;

/// <summary>
/// Demo responses of one workflow (<c>workflows[].demo</c> in <c>module.yaml</c>, e.g. <c>demo/joke.yaml</c>).
/// Optional: in Demo mode the engine answers each agent step with the response declared here instead of calling a model.
/// </summary>
public sealed class DemoFile
{
    /// <summary>Responses by step id (the left part of the agent's <c>"&lt;stepId&gt;|&lt;agentId&gt;"</c> name).</summary>
    public Dictionary<string, DemoStep> Steps { get; init; } = [];
}

/// <summary>
/// The demo response of one agent step. The first rule whose <see cref="DemoRule.When"/> matches the rendered prompt
/// wins; otherwise <see cref="Output"/> is used. Strings in an output accept <c>{name}</c> placeholders filled from
/// <see cref="Extract"/> and <c>{prompt}</c> (the whole trimmed prompt).
/// </summary>
public sealed class DemoStep
{
    /// <summary>Values read from the rendered prompt, by placeholder name.</summary>
    public Dictionary<string, DemoExtract> Extract { get; init; } = [];

    /// <summary>Ordered rules; the first match wins.</summary>
    public List<DemoRule> Rules { get; init; } = [];

    /// <summary>Output used when no rule matches (or the only output when there are no rules).</summary>
    public JsonObject? Output { get; init; }
}

/// <summary>
/// Reads one value from the rendered prompt: <see cref="Field"/> takes the text after a <c>Label:</c> line,
/// <see cref="Pattern"/> takes the first capture group of a regex (or the whole match). The value is trimmed;
/// when it is empty, <see cref="Default"/> is used (it may contain <c>{prompt}</c>).
/// </summary>
public sealed class DemoExtract
{
    public string? Field { get; init; }

    public string? Pattern { get; init; }

    public string? Default { get; init; }
}

/// <summary>An output chosen when the regex <see cref="When"/> matches the rendered prompt (case-insensitive).</summary>
public sealed class DemoRule
{
    public required string When { get; init; }

    public required JsonObject Output { get; init; }
}
