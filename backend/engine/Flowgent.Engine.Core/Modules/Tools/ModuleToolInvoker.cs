namespace Flowgent.Engine.Core.Modules.Tools;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Core.Configuration;
using Flowgent.Engine.Core.Modules.Client;
using Flowgent.Engine.Core.Runs.Execution;
using Microsoft.Extensions.Options;

/// <summary>
/// Calls a tool of a module service and checks its input and output against the JSON schemas declared by the module.
/// It uses <see cref="IModuleClient"/> for the HTTP call, <see cref="StepSchemas"/> for validation and
/// <see cref="EngineOptions"/> for the default timeout.
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
        ToolValidation.ValidateInput(module, tool, input, callerId, toolName);

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
            throw ToolValidation.ToStepFailure(callerId, ex);
        }

        ToolValidation.ValidateOutput(module, tool, output, callerId, toolName);

        return output;
    }
}
