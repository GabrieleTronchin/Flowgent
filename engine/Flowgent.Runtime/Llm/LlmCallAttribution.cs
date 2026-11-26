namespace Flowgent.Runtime.Llm;

using Microsoft.Extensions.AI;

/// <summary>
/// Engine-internal values carried in <see cref="ChatOptions.AdditionalProperties"/>: telemetry attribution and
/// the precomputed Demo response. The telemetry middleware removes them before the call reaches a provider.
/// </summary>
public static class LlmCallAttribution
{
    public const string AgentIdKey = "flowgent.telemetry.agentId";
    public const string ModuleIdKey = "flowgent.telemetry.moduleId";
    public const string WorkflowIdKey = "flowgent.telemetry.workflowId";
    public const string RunIdKey = "flowgent.telemetry.runId";
    public const string StepIdKey = "flowgent.telemetry.stepId";
    public const string DemoResponseKey = "flowgent.demo.response";

    internal static readonly string[] AllKeys = [AgentIdKey, ModuleIdKey, WorkflowIdKey, RunIdKey, StepIdKey, DemoResponseKey];

    /// <summary>Returns a copy of <paramref name="options"/> attributed to the given agent (and run/step when known).</summary>
    public static ChatOptions ForAgent(
        ChatOptions options,
        string? agentId,
        string? moduleId = null,
        string? workflowId = null,
        Guid? runId = null,
        string? stepId = null)
    {
        var clone = options.Clone();
        clone.AdditionalProperties ??= [];
        Set(clone, AgentIdKey, agentId);
        Set(clone, ModuleIdKey, moduleId);
        Set(clone, WorkflowIdKey, workflowId);
        Set(clone, RunIdKey, runId?.ToString());
        Set(clone, StepIdKey, stepId);
        return clone;
    }

    public static ChatOptions WithDemoResponse(ChatOptions options, string json)
    {
        var clone = options.Clone();
        clone.AdditionalProperties ??= [];
        clone.AdditionalProperties[DemoResponseKey] = json;
        return clone;
    }

    internal static string? Get(ChatOptions? options, string key) =>
        options?.AdditionalProperties is { } props && props.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static void Set(ChatOptions options, string key, string? value)
    {
        if (value is not null)
        {
            options.AdditionalProperties![key] = value;
        }
    }
}
