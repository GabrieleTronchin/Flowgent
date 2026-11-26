namespace Flowgent.Definitions.Model;

/// <summary>Manifest-side outcome metadata for one terminal step of a workflow.</summary>
public sealed class TerminalRef
{
    public required string Id { get; init; }

    public required WorkflowOutcome Outcome { get; init; }
}
