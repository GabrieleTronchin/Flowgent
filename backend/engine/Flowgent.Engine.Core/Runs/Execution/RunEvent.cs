namespace Flowgent.Engine.Core.Runs.Execution;

using System.Text.Json.Nodes;
using Flowgent.Definitions.Model;

/// <summary>
/// What happens during a run, as reported by an <see cref="IRunDriver"/>. The orchestrator reacts to these events
/// and never sees any workflow-framework type: the driver translates the framework's own events into these records.
/// </summary>
public abstract record RunEvent
{
    /// <summary>A logical step began executing.</summary>
    public sealed record StepStarted(string StepId) : RunEvent;

    /// <summary>A logical step finished; <see cref="Output"/> is the <c>Global.&lt;stepId&gt;</c> JSON value read from run state.</summary>
    public sealed record StepCompleted(string StepId, JsonNode? Output, string? Detail) : RunEvent;

    /// <summary>A human-input gate surfaced and the run is awaiting a decision on <see cref="StepId"/>.</summary>
    public sealed record AwaitingInput(string StepId, string? Prompt) : RunEvent;

    /// <summary>The workflow reached a terminal and produced its final output.</summary>
    public sealed record Completed(string TerminalId, WorkflowOutcome Outcome, string? Message, JsonNode? Output) : RunEvent;

    /// <summary>The workflow failed (an executor error or a workflow-level error).</summary>
    public sealed record Failed(string? StepId, string Reason) : RunEvent;
}
