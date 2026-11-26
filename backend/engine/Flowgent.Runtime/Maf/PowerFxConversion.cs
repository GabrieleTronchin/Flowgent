namespace Flowgent.Runtime.Maf;

using System.Text.Json.Nodes;

/// <summary>
/// Converts values across the PowerFx boundary as JSON strings (design §11). Every value the host reads out of a
/// run (a <c>Global.&lt;stepId&gt;</c> variable) or injects into it (a demo output, the intake object) crosses as a
/// JSON-serialized string, read with the spike-proven <c>First(...).Value</c> accessor on the PowerFx side; there is
/// no <c>FormulaValue</c> ↔ <c>JsonNode</c> record binding. These helpers are the single place that serialization
/// happens so the shape stays consistent with the authored workflow YAML.
/// </summary>
public static class PowerFxConversion
{
    /// <summary>Serializes a node to the JSON string stored in a <c>Global.*</c> variable. Null becomes the literal <c>"null"</c>.</summary>
    public static string ToJsonString(JsonNode? node) => node is null ? "null" : node.ToJsonString();

    /// <summary>
    /// Parses a JSON string read back from a <c>Global.*</c> variable into a node. Returns null for null/empty input or
    /// the literal <c>"null"</c>; a non-JSON string is wrapped as a JSON string value so the host always gets a node.
    /// </summary>
    public static JsonNode? FromJsonString(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "null")
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value);
        }
        catch (System.Text.Json.JsonException)
        {
            // A non-JSON string surfaces verbatim (the PROBE-C `.Value` path): carry it as a JSON string value.
            return JsonValue.Create(value);
        }
    }
}
