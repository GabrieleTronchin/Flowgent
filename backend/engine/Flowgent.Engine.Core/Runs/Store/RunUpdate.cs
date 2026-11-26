namespace Flowgent.Engine.Core.Runs.Store;

using System.Threading.Channels;
using Flowgent.Contracts.Runs;

/// <summary>A run transition published to stream subscribers (gRPC).</summary>
public sealed record RunUpdate(
    Guid RunId,
    string ModuleId,
    string WorkflowId,
    string? StepId,
    RunStatus Status,
    RunEventKind Kind,
    DateTimeOffset Timestamp,
    string? Detail,
    string? Title);
