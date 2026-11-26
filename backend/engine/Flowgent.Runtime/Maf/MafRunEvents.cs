namespace Flowgent.Runtime.Maf;

using System.Text.Json.Nodes;
using Flowgent.Definitions.Model;

/// <summary>
/// Neutral run events the <see cref="MafRunDriver"/> emits from the declarative stream, so <c>RunOrchestrator</c>
/// never sees a <c>Microsoft.Agents.AI.Workflows.Declarative</c> type (design §8, NFR-1). Each declarative event
/// (<c>MessageActivityEvent</c>, <c>WorkflowOutputEvent</c>, <c>WorkflowErrorEvent</c>, <c>RequestInfoEvent</c>,
/// per-action events) is translated into one of these records before it crosses the seam boundary.
/// </summary>
public abstract record MafRunEvent
{
    /// <summary>A logical step began executing.</summary>
    public sealed record StepStarted(string StepId) : MafRunEvent;

    /// <summary>A logical step finished; <see cref="Output"/> is the <c>Global.&lt;stepId&gt;</c> JSON value read from run state.</summary>
    public sealed record StepCompleted(string StepId, JsonNode? Output, string? Detail) : MafRunEvent;

    /// <summary>A human-input gate surfaced and the run is awaiting a decision on <see cref="StepId"/>.</summary>
    public sealed record AwaitingInput(string StepId, string? Prompt) : MafRunEvent;

    /// <summary>The workflow reached a terminal and produced its final output.</summary>
    public sealed record Completed(string TerminalId, WorkflowOutcome Outcome, string? Message, JsonNode? Output) : MafRunEvent;

    /// <summary>The workflow failed (an executor error or a workflow-level error).</summary>
    public sealed record Failed(string? StepId, string Reason) : MafRunEvent;
}

/// <summary>Decision supplied to resume a human-input gate; serialized to the <c>{decision, reason}</c> JSON the YAML parses.</summary>
public sealed record MafHumanDecision(bool Approved, string? Reason)
{
    public string Decision => Approved ? "approved" : "rejected";

    public JsonObject ToJson() => new() { ["decision"] = Decision, ["reason"] = Reason };
}
