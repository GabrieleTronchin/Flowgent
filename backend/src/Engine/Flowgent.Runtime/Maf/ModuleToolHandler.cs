namespace Flowgent.Runtime.Maf;

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Modules;

/// <summary>
/// The seam's <see cref="IMcpToolHandler"/> (design §5.2, §12), patterned on
/// <c>spike/Spike.MafDeclarative/SpikeMcpToolHandler.cs</c>. The declarative <c>InvokeMcpTool</c> action calls this
/// with the <c>serverLabel</c>/<c>toolName</c>/arguments from the YAML; the handler maps <c>serverLabel</c>→module id
/// and <c>toolName</c>→module tool name, invokes the tool through the retained <see cref="ModuleToolInvoker"/> over the
/// live per-run <see cref="ModuleDefinition"/>, and returns the output as exactly ONE non-numeric JSON string in one
/// <see cref="TextContent"/>. The non-numeric invariant is mandatory: a bare top-level number crashes the
/// <c>InvokeMcpTool</c> PowerFx binding with "Unsupported element type: Double" (spike F4), so a bare number is wrapped.
/// </summary>
public sealed class ModuleToolHandler(RunContext run, ModuleToolInvoker invoker) : IMcpToolHandler
{
    private readonly RunContext _run = run;
    private readonly ModuleToolInvoker _invoker = invoker;

    /// <summary>
    /// Reserved built-in tool name a workflow calls to read the run's intake input as a JSON-object string.
    /// PowerFx has no <c>ParseJSON</c> (PROBE-C), so a workflow cannot split the trigger message text into fields;
    /// instead it invokes this tool (<c>serverLabel: flowgent</c>) whose JSON-object-string result parses to a record
    /// read as <c>First(Local.x).&lt;field&gt;</c> (PROBE-A). Faithful: it reuses the same proven MCP string boundary.
    /// </summary>
    public const string InputToolName = "flowgent.input";

    /// <summary>
    /// Reserved built-in tool a workflow calls to turn an arbitrary JSON string (e.g. an agent's structured answer) into
    /// a PowerFx record: it echoes the <c>payload</c> argument verbatim as a JSON-object string read as
    /// <c>First(Local.x).&lt;field&gt;</c> (PROBE-A). This is the ParseJSON-free way to read a field out of a JSON string
    /// (PROBE-C: no <c>ParseJSON</c>); faithful because it reuses the same proven MCP string boundary.
    /// </summary>
    public const string ParseToolName = "flowgent.parse";

    public async Task<McpServerToolResultContent> InvokeToolAsync(
        string serverUrl,
        string? serverLabel,
        string toolName,
        IDictionary<string, object?>? arguments,
        IDictionary<string, string>? headers,
        string? connectionName,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(toolName, InputToolName, StringComparison.Ordinal))
        {
            return new McpServerToolResultContent($"mcp-{Guid.NewGuid():N}") { Outputs = [new TextContent(Serialize(_run.Input))] };
        }

        if (string.Equals(toolName, ParseToolName, StringComparison.Ordinal))
        {
            var payload = arguments is not null && arguments.TryGetValue("payload", out var p) ? p?.ToString() : null;
            return new McpServerToolResultContent($"mcp-{Guid.NewGuid():N}") { Outputs = [new TextContent(string.IsNullOrEmpty(payload) ? "null" : payload)] };
        }

        var input = ToJsonInput(arguments);
        var result = await _invoker.InvokeAsync(
            _run.Module,
            _run.RunId,
            _run.Workflow.Id,
            toolName,
            toolName,
            input,
            stepTimeoutSeconds: null,
            cancellationToken).ConfigureAwait(false);

        var callId = $"mcp-{Guid.NewGuid():N}";
        return new McpServerToolResultContent(callId)
        {
            Outputs = [new TextContent(Serialize(result))],
        };
    }

    /// <summary>Serializes the tool output to a single JSON string, wrapping a bare top-level number so the PowerFx cell is never a Double.</summary>
    internal static string Serialize(JsonNode? result) =>
        result switch
        {
            null => "null",
            JsonValue value when value.GetValueKind() == JsonValueKind.Number => new JsonObject { ["value"] = value.DeepClone() }.ToJsonString(),
            _ => result.ToJsonString(),
        };

    private static JsonObject ToJsonInput(IDictionary<string, object?>? arguments)
    {
        var input = new JsonObject();
        if (arguments is null)
        {
            return input;
        }

        foreach (var (key, value) in arguments)
        {
            input[key] = ToNode(value);
        }

        return input;
    }

    private static JsonNode? ToNode(object? value) =>
        value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            decimal m => JsonValue.Create(m),
            _ => JsonValue.Create(value.ToString()),
        };
}
