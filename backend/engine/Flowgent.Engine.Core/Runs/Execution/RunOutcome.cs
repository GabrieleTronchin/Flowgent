namespace Flowgent.Engine.Core.Runs.Execution;

using System.Text.Json.Nodes;
using Flowgent.Definitions.Model;

/// <summary>Output yielded by terminal steps.</summary>
public sealed record RunOutcome(string StepId, WorkflowOutcome Outcome, string? Message, JsonNode? Output);
