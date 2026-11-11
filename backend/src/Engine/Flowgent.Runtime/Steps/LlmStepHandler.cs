namespace Flowgent.Runtime.Steps;

using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Llm;
using Flowgent.Runtime.Modules;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

/// <summary>
/// <c>llm</c> steps: renders the prompts, requests a structured response matching the output schema,
/// validates it and stores it. In Demo mode the response is computed from the step's <c>demo:</c> block.
/// </summary>
internal sealed class LlmStepHandler(IChatClient chatClient, IOptions<LlmOptions> llmOptions, ToolStepHandler tools) : IStepHandler
{
    public string Kind => StepKinds.Llm;

    public bool RecordsStart => true;

    public async Task<JsonNode?> ExecuteAsync(StepDefinition step, RunContext run, CancellationToken ct)
    {
        var llm = (LlmStep)step;
        var templates = run.Templates;
        var scope = run.CreateScope();

        // The agent of this call (step agent, else the run's assigned agent) is visible to prompts as `agent`.
        var agentId = llm.Agent is null ? run.AssignedAgentId : templates.Render(llm.Agent, scope);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            agentId = null;
        }

        scope = scope.With("agent", AgentNode(run, agentId));

        var systemPrompt = llm.SystemPrompt is null ? null : templates.Render(run.Module.ResolveText(llm.SystemPrompt), scope);
        var prompt = templates.Render(run.Module.ResolveText(llm.Prompt), scope);
        var schemaNode = run.Module.GetSchemaNode(llm.Output.Schema);

        List<ChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }

        messages.Add(new ChatMessage(ChatRole.User, prompt));

        var options = LlmCallAttribution.ForAgent(
            new ChatOptions
            {
                Temperature = llm.Temperature,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    JsonSerializer.SerializeToElement(schemaNode),
                    schemaName: step.Id,
                    schemaDescription: step.Name),
            },
            agentId,
            run.Module.Id,
            run.Workflow.Id,
            run.RunId,
            step.Id);

        if (llmOptions.Value.IsDemo)
        {
            var demo = await ComputeDemoResponseAsync(llm, run, prompt, systemPrompt, ct);
            options = LlmCallAttribution.WithDemoResponse(options, demo.ToJsonString());
        }

        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(messages, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new StepFailedException(step.Id, $"LLM call failed for step '{step.Id}': {ex.Message}", ex);
        }

        JsonNode? output;
        try
        {
            output = JsonNode.Parse(StripCodeFence(response.Text));
        }
        catch (JsonException)
        {
            throw new StepFailedException(step.Id, $"LLM response for step '{step.Id}' is not valid JSON.");
        }

        StepSchemas.Validate(run.Module, llm.Output.Schema, output, step.Id, $"LLM response for step '{step.Id}'");
        return output;
    }

    private async Task<JsonNode> ComputeDemoResponseAsync(LlmStep llm, RunContext run, string prompt, string? systemPrompt, CancellationToken ct)
    {
        var demo = llm.Demo ?? throw new StepFailedException(llm.Id, $"Step '{llm.Id}' has no demo response; Demo mode requires a demo: block.");
        var templates = run.Templates;
        var scope = run.CreateScope();

        if (demo.Tool is not null)
        {
            var input = new JsonObject { ["stepId"] = llm.Id, ["prompt"] = prompt, ["systemPrompt"] = systemPrompt };
            return await tools.InvokeAsync(run, llm.Id, demo.Tool, input, null, ct)
                ?? throw new StepFailedException(llm.Id, $"Demo tool '{demo.Tool}' returned no output.");
        }

        if (demo.Rules is { Count: > 0 } rules)
        {
            var value = templates.Render(demo.Value ?? "", scope);
            var matches = RuleMatcher.Match(rules, value, ignoreCase: true, scope, templates);
            return templates.EvaluateValue(matches.Count > 0 ? rules[matches[0]].Output : demo.Default, scope) ?? new JsonObject();
        }

        return templates.EvaluateValue(demo.Output, scope) ?? new JsonObject();
    }

    private static JsonObject? AgentNode(RunContext run, string? agentId) =>
        agentId is not null && run.Module.FindAgent(agentId) is { } agent
            ? new JsonObject
            {
                ["id"] = agent.Id,
                ["name"] = agent.Name,
                ["group"] = agent.Group,
                ["role"] = agent.Role,
                ["persona"] = agent.Persona,
            }
            : null;

    /// <summary>Some providers wrap JSON in a markdown code fence even with structured output.</summary>
    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine > 0 && lastFence > firstNewLine ? trimmed[(firstNewLine + 1)..lastFence].Trim() : trimmed;
    }
}

/// <summary>
/// <c>tool</c> steps: call a Tool of the owning module service and validate its output.
/// The invocation logic lives in the retained <see cref="ModuleToolInvoker"/>; this handler only
/// shapes the step input and forwards to it, so deleting <c>Steps/*</c> later keeps tool parity.
/// </summary>
internal sealed class ToolStepHandler(ModuleToolInvoker invoker) : IStepHandler
{
    public string Kind => StepKinds.Tool;

    public bool RecordsStart => true;

    public Task<JsonNode?> ExecuteAsync(StepDefinition step, RunContext run, CancellationToken ct)
    {
        var tool = (ToolStep)step;
        var input = run.Templates.EvaluateValue(tool.Input ?? [], run.CreateScope());
        return InvokeAsync(run, step.Id, tool.Tool, input, tool.TimeoutSeconds, ct);
    }

    public Task<JsonNode?> InvokeAsync(RunContext run, string stepId, string toolName, JsonNode? input, int? stepTimeoutSeconds, CancellationToken ct) =>
        invoker.InvokeAsync(run.Module, run.RunId, run.Workflow.Id, stepId, toolName, input, stepTimeoutSeconds, ct);
}
