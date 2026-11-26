namespace Flowgent.Runtime.Modules;

using System.Text.Json.Nodes;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Schemas;
using Flowgent.Runtime.Execution;

/// <summary>
/// Validates step/tool inputs and outputs against JSON Schemas with consistent failure messages.
/// Retained (moved out of <c>Steps/*</c>) so it survives the step-handler deletion; used by
/// <see cref="ModuleToolInvoker"/> and the remaining step handlers.
/// </summary>
public static class StepSchemas
{
    public static void Validate(ModuleDefinition module, SchemaSource source, JsonNode? value, string stepId, string what)
    {
        var errors = JsonSchemaValidator.Validate(module.GetSchema(source), value);
        if (errors.Count > 0)
        {
            throw new StepFailedException(stepId, $"{what} does not match its schema: {string.Join("; ", errors.Take(5))}");
        }
    }
}
