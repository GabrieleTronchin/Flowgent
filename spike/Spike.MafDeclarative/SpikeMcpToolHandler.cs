using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;

namespace Spike.MafDeclarative;

/// <summary>
/// GOAL F4: the DECLARATIVE MCP path (NOT agent-level).
///
/// The declarative action <c>InvokeMcpTool</c> (BotElementKind.InvokeMcpTool) does NOT
/// let the LLM decide anything: at runtime the engine calls
/// <see cref="IMcpToolHandler.InvokeToolAsync"/> with the server url/label, tool name and
/// the arguments evaluated from the YAML, and binds the returned
/// <see cref="McpServerToolResultContent"/> into the action's <c>output.result</c>
/// workflow variable. This is a deterministic, explicit tool call.
///
/// Why a custom handler instead of the built-in <c>DefaultMcpToolHandler</c>:
/// <c>DefaultMcpToolHandler</c> connects to a real HTTP(S) MCP endpoint (it takes an
/// httpClientProvider keyed by serverUrl and opens a Streamable-HTTP session). Our spike
/// has no HTTP port — the MCP server is wired over in-memory pipes (see
/// <see cref="McpWordCountServer"/>). So we implement <see cref="IMcpToolHandler"/>
/// ourselves and route the call straight to the already-connected in-process MCP client.
/// The <paramref name="serverUrl"/> from the YAML is used only as a label here.
/// </summary>
public sealed class SpikeMcpToolHandler(McpWordCountServer mcp) : IMcpToolHandler
{
    private readonly McpWordCountServer _mcp = mcp;

    public async Task<McpServerToolResultContent> InvokeToolAsync(
        string serverUrl,
        string? serverLabel,
        string toolName,
        IDictionary<string, object?>? arguments,
        IDictionary<string, string>? headers,
        string? connectionName,
        CancellationToken cancellationToken = default)
    {
        var argText = arguments is null
            ? "(none)"
            : string.Join(", ", arguments.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        Console.WriteLine(
            $"[SpikeMcpToolHandler] InvokeToolAsync -> serverUrl='{serverUrl}' label='{serverLabel}' " +
            $"tool='{toolName}' args=[{argText}] (NO LLM in this path)");

        // Route to the in-process MCP client over the in-memory pipe transport.
        var result = await _mcp.CallToolAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);

        // The declarative engine expects an McpServerToolResultContent; its Outputs
        // carry the AIContent returned by the tool. We surface the raw MCP content blocks.
        var callId = $"mcp-{Guid.NewGuid():N}";
        var content = new McpServerToolResultContent(callId)
        {
            Outputs = [.. result],
        };

        var preview = string.Join(" | ", content.Outputs!.OfType<TextContent>().Select(t => t.Text ?? string.Empty));
        Console.WriteLine($"[SpikeMcpToolHandler] tool returned {content.Outputs.Count} content block(s): \"{preview}\"");
        return content;
    }
}
