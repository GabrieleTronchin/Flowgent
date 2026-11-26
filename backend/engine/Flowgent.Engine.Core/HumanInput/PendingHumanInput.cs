namespace Flowgent.Engine.Core.HumanInput;

using Flowgent.Contracts.Api;
using Flowgent.Engine.Core.Runs;

/// <summary>A run waiting for a decision.</summary>
public sealed record PendingHumanInput(
    Guid RunId,
    string ModuleId,
    string WorkflowId,
    string StepId,
    string? RunTitle,
    ReviewPayload Review,
    DateTimeOffset RequestedAt,
    TaskCompletionSource<HumanDecision> Completion)
{
    public HumanInputInfo ToInfo() => new(RunId, ModuleId, WorkflowId, StepId, RunTitle, Review, RequestedAt);
}
