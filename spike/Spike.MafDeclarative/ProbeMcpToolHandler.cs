using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;

namespace Spike.MafDeclarative;

/// <summary>
/// PROBE-A / PROBE-C handler. Unlike <see cref="SpikeMcpToolHandler"/> (which routes to a
/// real in-process MCP server), this handler returns a deterministic canned result per tool
/// name so the probes can exercise the exact value shapes the migration depends on:
///   - "locateComponent" -> a JSON-serialized OBJECT string (structured tool result).
///   - "malformed"       -> a NON-JSON string (to observe ParseJSON malformed behavior).
/// Every result is one non-numeric string in one TextContent, matching the design's
/// "JSON-string across the PowerFx boundary" invariant (never a bare number).
/// </summary>
public sealed class ProbeMcpToolHandler : IMcpToolHandler
{
    public Task<McpServerToolResultContent> InvokeToolAsync(
        string serverUrl,
        string? serverLabel,
        string toolName,
        IDictionary<string, object?>? arguments,
        IDictionary<string, string>? headers,
        string? connectionName,
        CancellationToken cancellationToken = default)
    {
        var text = toolName switch
        {
            "locateComponent" =>
                "{\"found\":true,\"component\":\"Checkout\",\"path\":\"src/Checkout.cs\",\"language\":\"csharp\"}",
            "malformed" => "not-json-at-all",
            _ => "{}",
        };

        Console.WriteLine($"[ProbeMcpToolHandler] tool='{toolName}' -> TextContent (string, len {text.Length}): {text}");

        var callId = $"probe-{Guid.NewGuid():N}";
        var content = new McpServerToolResultContent(callId)
        {
            Outputs = [new TextContent(text)],
        };
        return Task.FromResult(content);
    }
}
