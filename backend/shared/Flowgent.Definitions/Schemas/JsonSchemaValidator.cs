namespace Flowgent.Definitions.Schemas;

using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

/// <summary>A schema violation: JSON pointer of the offending value and a readable message.</summary>
public sealed record SchemaError(string Pointer, string Message)
{
    public override string ToString() => Pointer.Length == 0 ? Message : $"{Pointer}: {Message}";
}

/// <summary>Validates JSON values against JSON Schemas (draft 2020-12) and returns flat, readable errors.</summary>
public static class JsonSchemaValidator
{
    // Applicator keywords only summarise errors already reported by their sub-schemas.
    private static readonly HashSet<string> AggregateKeywords =
    [
        "properties", "patternProperties", "items", "prefixItems", "allOf", "$ref", "$dynamicRef",
        "dependentSchemas", "contains", "then", "else", "unevaluatedItems",
    ];

    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List };

    public static JsonSchema Parse(JsonNode schema) => JsonSchema.FromText(schema.ToJsonString());

    public static IReadOnlyList<SchemaError> Validate(JsonSchema schema, JsonNode? instance)
    {
        using var document = JsonDocument.Parse(instance?.ToJsonString() ?? "null");
        var result = schema.Evaluate(document.RootElement, Options);
        if (result.IsValid)
        {
            return [];
        }

        var errors = new List<SchemaError>();
        foreach (var detail in result.Details ?? [])
        {
            if (detail.Errors is not { Count: > 0 })
            {
                continue;
            }

            var pointer = detail.InstanceLocation.ToString();
            var evaluationPath = detail.EvaluationPath.ToString();
            foreach (var (keyword, message) in detail.Errors)
            {
                if (AggregateKeywords.Contains(keyword))
                {
                    continue;
                }

                if (keyword == "additionalProperties" || keyword == "unevaluatedProperties")
                {
                    // Reported again on each offending property (false schema) with its location.
                    continue;
                }

                var text = keyword.Length == 0 && (evaluationPath.EndsWith("/additionalProperties", StringComparison.Ordinal)
                    || evaluationPath.EndsWith("/unevaluatedProperties", StringComparison.Ordinal))
                    ? "Property is not allowed."
                    : message;

                errors.Add(new SchemaError(pointer, text));
            }
        }

        if (errors.Count == 0)
        {
            errors.Add(new SchemaError("", "The value does not match the schema."));
        }

        return errors.Distinct().ToList();
    }
}
