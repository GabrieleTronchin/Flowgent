namespace Flowgent.Runtime.Modules;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Configuration;
using Flowgent.Runtime.Execution;
using Microsoft.Extensions.Options;

/// <summary>
/// Calls a module tool and validates its input/output. Retained component extracted verbatim from
/// <c>ToolStepHandler.InvokeAsync</c> so tool invocation survives the <c>Steps/*</c> deletion; it
/// depends only on <see cref="IModuleClient"/>, <see cref="ModuleDefinition"/>, <see cref="StepSchemas"/>
/// and <see cref="EngineOptions"/>, never on anything under <c>Steps/*</c>.
/// </summary>
public sealed class ModuleToolInvoker(IModuleClient moduleClient, IOptions<EngineOptions> engineOptions)
{
    /// <summary>
    /// Looks up <paramref name="toolName"/> on <paramref name="module"/>, validates the input, resolves the
    /// effective timeout (tool → caller step → module → engine default), invokes the tool and validates the
    /// output. A <see cref="ToolInvocationException"/> is mapped to a <see cref="StepFailedException"/>.
    /// </summary>
    public async Task<JsonNode?> InvokeAsync(
        ModuleDefinition module,
        Guid runId,
        string workflowId,
        string callerId,
        string toolName,
        JsonNode? input,
        int? stepTimeoutSeconds,
        CancellationToken ct)
    {
        var tool = module.FindTool(toolName) ?? throw new StepFailedException(callerId, $"Tool '{toolName}' is not declared.");
        if (tool.Input is not null)
        {
            StepSchemas.Validate(module, tool.Input.Schema, input, callerId, $"Input of tool '{toolName}'");
        }

        var timeoutSeconds = tool.TimeoutSeconds ?? stepTimeoutSeconds
            ?? engineOptions.Value.Modules.FirstOrDefault(m => m.Id == module.Id)?.TimeoutSeconds
            ?? engineOptions.Value.DefaultToolTimeoutSeconds;

        JsonNode? output;
        try
        {
            output = await moduleClient.InvokeToolAsync(
                module.Id,
                toolName,
                new ToolInvocationRequest(runId, module.Id, workflowId, callerId, input),
                TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)),
                ct);
        }
        catch (ToolInvocationException ex)
        {
            throw new StepFailedException(callerId, ex.Message, ex);
        }

        if (tool.Output is not null)
        {
            StepSchemas.Validate(module, tool.Output.Schema, output, callerId, $"Output of tool '{toolName}'");
        }

        return output;
    }
}
