namespace Flowgent.Engine.Core.Runs.Execution;

/// <summary>
/// Runs one workflow from start to finish for <see cref="RunOrchestrator"/> and reports what happens as
/// <see cref="RunEvent"/> records. The only implementation today is <c>MafRunDriver</c>, which runs the workflow on
/// Microsoft Agent Framework; the interface keeps the orchestrator free of any framework type.
/// </summary>
public interface IRunDriver
{
    /// <summary>
    /// Builds and runs the workflow of <paramref name="run"/> to completion, calling <paramref name="onEvent"/> for each
    /// event and <paramref name="resolveDecision"/> when a human-input gate needs an answer. Returns the terminal event
    /// (Completed or Failed); cancellation of <paramref name="ct"/> surfaces as an <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<RunEvent> RunAsync(
        RunContext run,
        Func<RunEvent, Task> onEvent,
        ResolveDecisionAsync resolveDecision,
        CancellationToken ct);
}

/// <summary>Resolves the human decision for a gate on <paramref name="stepId"/>; cancellation ends the wait.</summary>
public delegate Task<RunHumanDecision> ResolveDecisionAsync(string stepId, string? prompt, CancellationToken ct);
