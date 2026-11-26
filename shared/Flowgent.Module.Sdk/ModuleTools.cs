namespace Flowgent.Module.Sdk;

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Context of a tool call made by a workflow step.</summary>
public sealed record ToolInvocation(Guid RunId, string ModuleId, string WorkflowId, string StepId, JsonNode? Input, ModuleDefinitionProvider Module);

/// <summary>A named operation of the module, called by <c>tool</c> steps (and by <c>demo.tool</c>).</summary>
public interface IModuleTool
{
    Task<JsonNode?> InvokeAsync(ToolInvocation invocation, CancellationToken ct);
}

/// <summary>Expected tool failure: returned to the engine as an error (the step fails with this message).</summary>
public sealed class ToolException(string message) : Exception(message);

/// <summary>Typed tool: input and output are (de)serialized as camelCase JSON.</summary>
public abstract class ModuleTool<TInput, TOutput> : IModuleTool
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JsonNode?> InvokeAsync(ToolInvocation invocation, CancellationToken ct)
    {
        TInput input;
        try
        {
            input = invocation.Input.Deserialize<TInput>(JsonOptions) ?? throw new ToolException("The tool input is missing.");
        }
        catch (JsonException ex)
        {
            throw new ToolException($"Invalid tool input: {ex.Message}");
        }

        var output = await InvokeAsync(input, invocation, ct);
        return JsonSerializer.SerializeToNode(output, JsonOptions);
    }

    protected abstract Task<TOutput> InvokeAsync(TInput input, ToolInvocation invocation, CancellationToken ct);
}

/// <summary>Tool names registered with <see cref="ModuleSdkExtensions.AddModuleTool{TTool}"/>.</summary>
public sealed class ModuleToolRegistry
{
    private readonly Dictionary<string, Type> _tools = new(StringComparer.Ordinal);

    internal void Add(string name, Type type)
    {
        if (!_tools.TryAdd(name, type))
        {
            throw new InvalidOperationException($"Tool '{name}' is registered more than once.");
        }
    }

    public bool Contains(string name) => _tools.ContainsKey(name);

    public IReadOnlyCollection<string> Names => _tools.Keys;

    internal IModuleTool? Resolve(IServiceProvider services, string name) =>
        _tools.TryGetValue(name, out var type) ? (IModuleTool)services.GetRequiredService(type) : null;
}
