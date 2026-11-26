namespace Flowgent.Engine.Core.Modules.Tools;

using System.Text.Json.Nodes;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Schemas;
using Flowgent.Engine.Core.Runs.Execution;

/// <summary>
/// Checks a step or tool input/output against a JSON schema and fails the step with a consistent message.
/// Used by <see cref="ModuleToolInvoker"/>.
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
