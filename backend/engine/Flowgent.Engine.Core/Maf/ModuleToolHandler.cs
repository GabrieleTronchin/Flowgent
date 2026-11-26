namespace Flowgent.Engine.Core.Maf;

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Flowgent.Engine.Core.Configuration;
using Flowgent.Engine.Core.Execution;
using Flowgent.Engine.Core.Modules;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// The seam's <see cref="IMcpToolHandler"/>. The declarative
/// <c>InvokeMcpTool</c> action calls this with the <c>serverLabel</c>/<c>toolName</c>/arguments from the YAML; the
/// handler resolves the route in a strict order:
/// <list type="number">
/// <item>reserved built-ins by tool name (<see cref="InputToolName"/>/<see cref="ParseToolName"/>) served in-process;</item>
/// <item>a <c>serverLabel</c> mapped in <see cref="EngineOptions.McpServers"/> → the MCP-over-HTTP branch (an entry whose
/// label is the reserved <c>flowgent</c>, ordinal-ignore-case, is logged and skipped — never HTTP-routable);</item>
/// <item>an unmapped label → the legacy in-process <see cref="ModuleToolInvoker"/> (which raises the existing
/// <c>Tool '&lt;name&gt;' is not declared.</c> failure when the tool is truly absent).</item>
/// </list>
/// Both branches return the tool output as exactly ONE non-numeric JSON string in one <see cref="TextContent"/>. The
/// non-numeric invariant is mandatory: a bare top-level number crashes the <c>InvokeMcpTool</c> PowerFx binding with
/// "Unsupported element type: Double", so a bare number is wrapped.
/// </summary>
public sealed class ModuleToolHandler : IMcpToolHandler
{
    private readonly RunContext _run;
    private readonly ModuleToolInvoker _invoker;
    private readonly McpClientPool _pool;
    private readonly EngineOptions _engineOptions;
    private readonly ILogger _logger;
    private readonly IReadOnlyDictionary<string, string> _labelToUrl;

    /// <summary>The reserved label that must never be HTTP-routed (its tools are the in-process built-ins).</summary>
    public const string ReservedLabel = "flowgent";

    public ModuleToolHandler(RunContext run, ModuleToolInvoker invoker, McpClientPool pool, EngineOptions engineOptions, ILogger? logger = null)
    {
        _run = run;
        _invoker = invoker;
        _pool = pool;
        _engineOptions = engineOptions;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _labelToUrl = BuildLabelMap(engineOptions.McpServers, _logger);
    }

    /// <summary>
    /// Reserved built-in tool name a workflow calls to read the run's intake input as a JSON-object string.
    /// PowerFx has no <c>ParseJSON</c>, so a workflow cannot split the trigger message text into fields;
    /// instead it invokes this tool (<c>serverLabel: flowgent</c>) whose JSON-object-string result parses to a record
    /// read as <c>First(Local.x).&lt;field&gt;</c>. It reuses the same MCP string boundary as every other tool.
    /// </summary>
    public const string InputToolName = "flowgent.input";

    /// <summary>
    /// Reserved built-in tool a workflow calls to turn an arbitrary JSON string (e.g. an agent's structured answer) into
    /// a PowerFx record: it echoes the <c>payload</c> argument verbatim as a JSON-object string read as
    /// <c>First(Local.x).&lt;field&gt;</c>. This is the ParseJSON-free way to read a field out of a JSON string
    /// (PowerFx has no <c>ParseJSON</c>), reusing the same MCP string boundary as every other tool.
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
        // (1) Reserved built-ins by tool name, served in-process.
        if (TryServeBuiltIn(toolName, arguments) is { } builtIn)
        {
            return builtIn;
        }

        var input = ToJsonInput(arguments);

        // (2) A mapped serverLabel → the MCP-over-HTTP branch.
        if (serverLabel is not null && _labelToUrl.TryGetValue(serverLabel, out var url))
        {
            var result = await InvokeOverHttpAsync(url, serverLabel, toolName, input, cancellationToken).ConfigureAwait(false);
            return TextResult(Serialize(result));
        }

        // (3) Unmapped label → the legacy in-process invoker (keeps the existing "not declared" failure).
        var inProcess = await _invoker.InvokeAsync(
            _run.Module,
            _run.RunId,
            _run.Workflow.Id,
            toolName,
            toolName,
            input,
            stepTimeoutSeconds: null,
            cancellationToken).ConfigureAwait(false);

        return TextResult(Serialize(inProcess));
    }

    private static McpServerToolResultContent TextResult(string text) =>
        new($"mcp-{Guid.NewGuid():N}") { Outputs = [new TextContent(text)] };

    /// <summary>Serves the reserved <c>flowgent.*</c> tools in-process; returns null for any other tool name.</summary>
    private McpServerToolResultContent? TryServeBuiltIn(string toolName, IDictionary<string, object?>? arguments)
    {
        if (string.Equals(toolName, InputToolName, StringComparison.Ordinal))
        {
            return TextResult(Serialize(_run.Input));
        }

        if (string.Equals(toolName, ParseToolName, StringComparison.Ordinal))
        {
            var payload = arguments is not null && arguments.TryGetValue("payload", out var p) ? p?.ToString() : null;
            return TextResult(string.IsNullOrEmpty(payload) ? "null" : payload);
        }

        return null;
    }

    /// <summary>
    /// The MCP-over-HTTP branch: validates the input, calls the bare wire tool name through a
    /// pooled <see cref="McpClient"/> under the effective timeout, evicts-and-reconnects once on a session/transport
    /// failure, then validates the output. <c>callerId</c> is the module-qualified tool name (mirrors the in-process
    /// path, which uses the tool name as the caller step id for failure attribution).
    /// </summary>
    private async Task<JsonNode?> InvokeOverHttpAsync(string url, string serverLabel, string toolName, JsonObject input, CancellationToken ct)
    {
        var callerId = toolName;

        // The module-qualified toolName is used for the schema lookup; the BARE name (segment after the last '.') goes
        // on the wire. No '.' → sent verbatim.
        var tool = _run.Module.FindTool(toolName) ?? throw new StepFailedException(callerId, $"Tool '{toolName}' is not declared.");
        var wireToolName = BareToolName(toolName);

        ToolValidation.ValidateInput(_run.Module, tool, input, callerId, toolName);

        var timeoutSeconds = tool.TimeoutSeconds
            ?? _engineOptions.Modules.FirstOrDefault(m => m.Id == _run.Module.Id)?.TimeoutSeconds
            ?? _engineOptions.DefaultToolTimeoutSeconds;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        CallToolResult callResult;
        try
        {
            callResult = await CallWithReconnectAsync(url, serverLabel, toolName, wireToolName, ToWireArguments(input), timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StepFailedException(callerId, $"MCP tool '{toolName}' timed out after {Math.Max(1, timeoutSeconds)}s.");
        }

        if (callResult.IsError == true)
        {
            throw new StepFailedException(callerId, ErrorMessage(callResult));
        }

        var output = ReadOutput(callResult);
        ToolValidation.ValidateOutput(_run.Module, tool, output, callerId, toolName);
        return output;
    }

    /// <summary>
    /// Calls the tool once; on a call-time session/transport failure evicts the pooled client, reconnects exactly once
    /// and retries the single call. A second failure is terminal (no further retry).
    /// </summary>
    private async Task<CallToolResult> CallWithReconnectAsync(
        string url,
        string serverLabel,
        string toolName,
        string wireToolName,
        IReadOnlyDictionary<string, object?>? args,
        CancellationToken callToken)
    {
        var client = await ConnectAsync(url, serverLabel, toolName, callToken).ConfigureAwait(false);
        try
        {
            return await client.CallToolAsync(wireToolName, args, cancellationToken: callToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not StepFailedException)
        {
            _logger.LogWarning(ex, "MCP call to '{Label}' ({Tool}) failed; evicting and reconnecting once.", serverLabel, wireToolName);
            _pool.Evict(url);
            return await RetryCallAsync(url, serverLabel, toolName, wireToolName, args, callToken).ConfigureAwait(false);
        }
    }

    private async Task<CallToolResult> RetryCallAsync(
        string url,
        string serverLabel,
        string toolName,
        string wireToolName,
        IReadOnlyDictionary<string, object?>? args,
        CancellationToken callToken)
    {
        var retryClient = await ConnectAsync(url, serverLabel, toolName, callToken).ConfigureAwait(false);
        try
        {
            return await retryClient.CallToolAsync(wireToolName, args, cancellationToken: callToken).ConfigureAwait(false);
        }
        catch (Exception retryEx) when (retryEx is not OperationCanceledException && retryEx is not StepFailedException)
        {
            throw new StepFailedException(toolName, $"MCP tool '{toolName}' failed after reconnect: {retryEx.Message}", retryEx);
        }
    }

    private async Task<McpClient> ConnectAsync(string url, string serverLabel, string callerId, CancellationToken ct)
    {
        try
        {
            return await _pool.GetAsync(url, serverLabel, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not StepFailedException)
        {
            throw new StepFailedException(callerId, $"MCP server '{serverLabel}' unavailable.", ex);
        }
    }

    /// <summary>Reads the single returned <see cref="TextContentBlock"/> payload as a <see cref="JsonNode"/>.</summary>
    private static JsonNode? ReadOutput(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text);
    }

    private static string ErrorMessage(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t));
        return string.IsNullOrEmpty(text) ? "MCP tool returned an error." : text;
    }

    /// <summary>The bare wire tool name: the segment after the last '.' (verbatim if there is no '.').</summary>
    internal static string BareToolName(string toolName)
    {
        var idx = toolName.LastIndexOf('.');
        return idx >= 0 && idx < toolName.Length - 1 ? toolName[(idx + 1)..] : toolName;
    }

    /// <summary>Builds the label→URL map, logging and skipping any entry reusing the reserved <c>flowgent</c> label.</summary>
    private static IReadOnlyDictionary<string, string> BuildLabelMap(IEnumerable<McpServerOptions> servers, ILogger logger)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var server in servers)
        {
            if (string.Equals(server.Label, ReservedLabel, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("Ignoring MCP server entry with reserved label '{Label}' ({Url}); the '{Reserved}' label is never HTTP-routable.", server.Label, RedactUrlForLog(server.Url), ReservedLabel);
                continue;
            }

            map[server.Label] = server.Url;
        }

        return map;
    }

    /// <summary>
    /// Redacts a configured MCP server URL for logging: strips any embedded credentials (userinfo),
    /// query and fragment, so a secret in the configured URL is never written to logs.
    /// </summary>
    private static string RedactUrlForLog(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "<empty>";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "<redacted>";
        }

        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString();
    }

    /// <summary>Serializes the tool output to a single JSON string, wrapping a bare top-level number so the PowerFx cell is never a Double.</summary>
    internal static string Serialize(JsonNode? result) =>
        result switch
        {
            null => "null",
            JsonValue value when value.GetValueKind() == JsonValueKind.Number => new JsonObject { ["value"] = value.DeepClone() }.ToJsonString(),
            _ => result.ToJsonString(),
        };

    private static IReadOnlyDictionary<string, object?>? ToWireArguments(JsonObject input)
    {
        if (input.Count == 0)
        {
            return null;
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in input)
        {
            dict[key] = value?.DeepClone();
        }

        return dict;
    }

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
