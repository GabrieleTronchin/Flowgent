namespace Flowgent.Engine.Core.Runs;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Runs;

/// <summary>
/// A change of a run: moves it to a step and/or status and appends one event (dual-write).
/// Optional fields are only applied when set.
/// </summary>
public sealed record RunTransition(
    Guid RunId,
    RunEventKind Kind,
    RunStatus Status,
    string? StepId,
    string? Detail,
    string? AssignedAgentId = null,
    JsonObject? StepOutputs = null,
    JsonNode? FinalOutput = null);
