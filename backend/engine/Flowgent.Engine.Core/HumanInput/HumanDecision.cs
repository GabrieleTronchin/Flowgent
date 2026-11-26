namespace Flowgent.Engine.Core.HumanInput;

using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Core.Runs;

/// <summary>Decision taken by an operator on a human-input step.</summary>
public sealed record HumanDecision(bool Approved, string? Reason)
{
    public string Value => Approved ? "approved" : "rejected";
}
