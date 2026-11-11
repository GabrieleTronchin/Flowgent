namespace Flowgent.Definitions.Validation;

using System.Text.Json.Nodes;
using Json.Schema;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Schemas;

/// <summary>
/// Envelope-level semantic checks the format schemas cannot express: manifest agent/tool declarations, tool and
/// workflow-input schema compilation, schema-file existence, and sample validation against the workflow input
/// schema. The declarative workflow document itself is not step-validated here; that is the MAF builder's job
/// (wired in by the engine as a trial build).
/// </summary>
internal static class EnvelopeValidator
{
    public static void Validate(
        ModuleManifest manifest,
        IReadOnlyList<WorkflowDescriptor> workflows,
        IReadOnlyList<SampleDocument> samples,
        IReadOnlyDictionary<string, string> files,
        Dictionary<string, (JsonObject, JsonSchema)> schemas,
        DefinitionErrors errors)
    {
        var context = new SchemaContext(files, schemas, errors);

        ValidateManifest(manifest, context);

        var workflowIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workflow in workflows)
        {
            if (!workflowIds.Add(workflow.Id))
            {
                errors.Add(workflow.File, null, null, $"Workflow id '{workflow.Id}' is declared more than once.");
            }

            context.RegisterSchema(workflow.InputSchema, workflow.File, null, $"input schema of workflow '{workflow.Id}'");
        }

        ValidateSamples(context, workflows, samples);
    }

    private static void ValidateManifest(ModuleManifest manifest, SchemaContext context)
    {
        const string file = ModuleManifest.FileName;

        foreach (var duplicate in manifest.Agents.GroupBy(a => a.Id).Where(g => g.Count() > 1))
        {
            context.Errors.Add(file, null, null, $"Agent '{duplicate.Key}' is declared more than once.");
        }

        foreach (var duplicate in manifest.Tools.GroupBy(t => t.Name).Where(g => g.Count() > 1))
        {
            context.Errors.Add(file, null, null, $"Tool '{duplicate.Key}' is declared more than once.");
        }

        foreach (var tool in manifest.Tools)
        {
            if (!tool.Name.StartsWith(manifest.Id + ".", StringComparison.Ordinal))
            {
                context.Errors.Add(file, null, null, $"Tool '{tool.Name}' must be prefixed with the module id ('{manifest.Id}.').");
            }

            if (tool.Input is not null)
            {
                context.RegisterSchema(tool.Input.Schema, file, null, $"input schema of tool '{tool.Name}'");
            }

            if (tool.Output is not null)
            {
                context.RegisterSchema(tool.Output.Schema, file, null, $"output schema of tool '{tool.Name}'");
            }
        }
    }

    private static void ValidateSamples(SchemaContext context, IReadOnlyList<WorkflowDescriptor> workflows, IReadOnlyList<SampleDocument> samples)
    {
        foreach (var duplicate in samples.GroupBy(s => s.Sample.Id).Where(g => g.Count() > 1))
        {
            context.Errors.Add(duplicate.First().File, null, null, $"Sample '{duplicate.Key}' is declared more than once.");
        }

        foreach (var doc in samples)
        {
            var sample = doc.Sample;
            var line = doc.Lines.Find($"/samples/{doc.Index}");
            var workflow = workflows.FirstOrDefault(w => w.Id == sample.Workflow);
            if (workflow is null)
            {
                context.Errors.Add(doc.File, null, line, $"Sample '{sample.Id}' references unknown workflow '{sample.Workflow}'.");
                continue;
            }

            if (!context.TryGetSchema(workflow.InputSchema, out var schema))
            {
                continue;
            }

            var schemaErrors = JsonSchemaValidator.Validate(schema, sample.Input ?? []);
            if (schemaErrors.Count > 0 && !sample.ExpectInvalid)
            {
                context.Errors.Add(doc.File, null, line,
                    $"Sample '{sample.Id}' does not match the input schema of '{workflow.Id}': {string.Join("; ", schemaErrors)}");
            }
            else if (schemaErrors.Count == 0 && sample.ExpectInvalid)
            {
                context.Errors.Warn(doc.File, null, line, $"Sample '{sample.Id}' is marked expectInvalid but matches the input schema.");
            }
        }
    }

    /// <summary>Compiles and caches the JSON Schemas referenced by the manifest and workflow inputs.</summary>
    private sealed class SchemaContext(
        IReadOnlyDictionary<string, string> files,
        Dictionary<string, (JsonObject, JsonSchema)> schemas,
        DefinitionErrors errors)
    {
        public DefinitionErrors Errors { get; } = errors;

        public bool TryGetSchema(SchemaSource source, out JsonSchema schema)
        {
            if (schemas.TryGetValue(source.Key, out var entry))
            {
                schema = entry.Item2;
                return true;
            }

            schema = null!;
            return false;
        }

        public JsonObject? RegisterSchema(SchemaSource source, string file, string? stepId, string what, int? line = null)
        {
            if (schemas.TryGetValue(source.Key, out var existing))
            {
                return existing.Item1;
            }

            JsonObject? node = source.Inline;
            if (source.File is { } path)
            {
                if (!DefinitionPaths.IsSafe(path) || !files.TryGetValue(DefinitionPaths.Normalize(path), out var text))
                {
                    Errors.Add(file, stepId, line, $"Schema file '{path}' for the {what} does not exist.");
                    return null;
                }

                try
                {
                    node = JsonNode.Parse(text) as JsonObject;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    Errors.Add(DefinitionPaths.Normalize(path), stepId, null, $"Invalid JSON in schema file '{path}': {ex.Message}");
                    return null;
                }

                if (node is null)
                {
                    Errors.Add(DefinitionPaths.Normalize(path), stepId, null, $"Schema file '{path}' must contain a JSON object.");
                    return null;
                }
            }

            try
            {
                var schema = JsonSchemaValidator.Parse(node!);
                schemas[source.Key] = (node!, schema);
                return node;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                Errors.Add(source.File ?? file, stepId, line, $"Invalid JSON Schema for the {what}: {ex.Message}");
                return null;
            }
        }
    }
}
