namespace Flowgent.Runtime.Modules;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Execution;

/// <summary>
/// Shared tool input/output schema validation and the <see cref="ToolInvocationException"/> →
/// <see cref="StepFailedException"/> mapping, factored out of <see cref="ModuleToolInvoker"/> so the
/// same checks can be reused by any tool-calling path (the in-process invoker and the MCP-over-HTTP
/// branch) without duplicating the validation/mapping logic. Behavior is identical to the original
/// inline calls: the schema is validated only when declared, the error prefix is
/// <c>"Input/Output of tool '&lt;name&gt;'"</c>, and a tool failure becomes a
/// <see cref="StepFailedException"/> carrying the same message and inner exception.
/// </summary>
internal static class ToolValidation
{
    /// <summary>Validates the tool input against its declared schema (no-op when the tool declares none).</summary>
    public static void ValidateInput(ModuleDefinition module, ToolDefinition tool, JsonNode? input, string callerId, string toolName)
    {
        if (tool.Input is not null)
        {
            StepSchemas.Validate(module, tool.Input.Schema, input, callerId, $"Input of tool '{toolName}'");
        }
    }

    /// <summary>Validates the tool output against its declared schema (no-op when the tool declares none).</summary>
    public static void ValidateOutput(ModuleDefinition module, ToolDefinition tool, JsonNode? output, string callerId, string toolName)
    {
        if (tool.Output is not null)
        {
            StepSchemas.Validate(module, tool.Output.Schema, output, callerId, $"Output of tool '{toolName}'");
        }
    }

    /// <summary>Maps a tool-invocation failure to a <see cref="StepFailedException"/> attributed to the caller step.</summary>
    public static StepFailedException ToStepFailure(string callerId, ToolInvocationException ex) =>
        new(callerId, ex.Message, ex);
}
