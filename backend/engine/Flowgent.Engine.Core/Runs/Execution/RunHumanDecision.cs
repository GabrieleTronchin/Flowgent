namespace Flowgent.Engine.Core.Runs.Execution;

using System.Text.Json.Nodes;

/// <summary>Decision supplied to resume a human-input gate; serialized to the <c>{decision, reason}</c> JSON the YAML parses.</summary>
public sealed record RunHumanDecision(bool Approved, string? Reason)
{
    public string Decision => Approved ? "approved" : "rejected";

    public JsonObject ToJson() => new() { ["decision"] = Decision, ["reason"] = Reason };
}
