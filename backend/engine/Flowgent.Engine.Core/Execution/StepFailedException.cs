namespace Flowgent.Engine.Core.Execution;

using Flowgent.Definitions;
using Flowgent.Definitions.Model;

/// <summary>A step failed for an expected reason (validation, rule, tool or LLM error); the run ends as Failed.</summary>
public sealed class StepFailedException(string stepId, string message, Exception? inner = null) : Exception(message, inner)
{
    public string StepId { get; } = stepId;
}
