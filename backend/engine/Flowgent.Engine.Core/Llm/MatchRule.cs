namespace Flowgent.Engine.Core.Llm;

using System.Text.Json.Nodes;

/// <summary>
/// A single regex rule and the output to select when it matches. Retained replacement for the
/// custom-format <c>RuleDefinition</c> (which is deleted with the step model in a later feature):
/// <see cref="RuleMatcher"/> works against this type, not the definition model.
/// </summary>
public sealed record MatchRule(string Pattern, JsonNode? Output);
