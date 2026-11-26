namespace Flowgent.Module.Sdk;

using System.Text.Json;
using System.Text.Json.Nodes;

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
