namespace Flowgent.Module.Sdk;

using System.Text.Json.Nodes;

/// <summary>A named operation of the module, called by <c>tool</c> steps (and by <c>demo.tool</c>).</summary>
public interface IModuleTool
{
    Task<JsonNode?> InvokeAsync(ToolInvocation invocation, CancellationToken ct);
}
