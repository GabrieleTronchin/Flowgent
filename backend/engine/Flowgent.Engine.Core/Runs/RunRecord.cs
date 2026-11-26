namespace Flowgent.Engine.Core.Runs;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;

/// <summary>A run as stored by the engine.</summary>
public sealed record RunRecord(
    Guid Id,
    string ModuleId,
    string WorkflowId,
    int WorkflowVersion,
    string? Title,
    JsonObject Input,
    RunStatus Status,
    string? CurrentStepId,
    string? Detail,
    string? AssignedAgentId,
    JsonObject StepOutputs,
    JsonNode? FinalOutput,
    Guid? InboxMessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdated)
{
    public RunSummary ToSummary() => new(Id, ModuleId, WorkflowId, WorkflowVersion, Title, Status, CurrentStepId, Detail, AssignedAgentId, CreatedAt, LastUpdated);

    public RunDetail ToDetail() => new(
        Id, ModuleId, WorkflowId, WorkflowVersion, Title, Status, CurrentStepId, Detail, AssignedAgentId, CreatedAt, LastUpdated,
        Input.DeepClone(), StepOutputs.DeepClone().AsObject(), FinalOutput?.DeepClone());
}
