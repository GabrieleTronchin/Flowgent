namespace Flowgent.Engine.Tests.Runtime;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Tests.Helpers;
using Flowgent.Runtime.Configuration;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Maf;
using Flowgent.Runtime.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

/// <summary>
/// FEAT-002 unit tests for the strict resolution order of <see cref="ModuleToolHandler"/> and the MCP-over-HTTP branch,
/// driven through a fake <see cref="IMcpTransportFactory"/> over in-memory pipes (no network). Covers: reserved
/// built-ins served in-process; a mapped label routed to the HTTP branch; the reserved <c>flowgent</c> label rejected;
/// an unmapped label falling back to the legacy in-process invoker; bare-name wire translation with qualified schema
/// lookup; the non-numeric wrapper; input/output schema validation; and exactly-one evict-and-reconnect on a call-time
/// session failure.
/// </summary>
public sealed class ModuleToolHandlerTests
{
    private const string MappedLabel = "demo";
    private const string MappedUrl = "http://demo-mcp";

    [Fact]
    public async Task ReservedInputTool_IsServedInProcess_FromRunInput()
    {
        var (handler, _, _, _) = Build(mappedLabel: null);

        var result = await handler.InvokeToolAsync("inproc://flowgent", "flowgent", ModuleToolHandler.InputToolName, null, null, null);

        var text = Text(result);
        var node = JsonNode.Parse(text)!;
        Assert.Equal("hello world", node["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReservedParseTool_EchoesPayload_InProcess()
    {
        var (handler, _, _, _) = Build(mappedLabel: null);
        var args = new Dictionary<string, object?> { ["payload"] = "{\"a\":1}" };

        var result = await handler.InvokeToolAsync("inproc://flowgent", "flowgent", ModuleToolHandler.ParseToolName, args, null, null);

        Assert.Equal("{\"a\":1}", Text(result));
    }

    [Fact]
    public async Task MappedLabel_IsRoutedOverHttp_AndReadsJsonResult()
    {
        await using var server = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{\"words\":2}"));
        var (handler, _, moduleClient, factory) = Build(mappedLabel: MappedLabel, server.ClientTransport);

        var args = new Dictionary<string, object?> { ["text"] = "two words" };
        var result = await handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null);

        Assert.Equal("{\"words\":2}", Text(result));
        // The HTTP branch was taken: the in-process invoker was never asked to call the module.
        await moduleClient.DidNotReceiveWithAnyArgs().InvokeToolAsync(default!, default!, default!, default, default);
        Assert.Single(factory.Creates);
        Assert.Equal((MappedUrl, MappedLabel), factory.Creates[0]);
    }

    [Fact]
    public async Task HttpBranch_SendsBareWireName_ButUsesQualifiedNameForSchemaLookup()
    {
        await using var server = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{\"words\":1}"));
        var (handler, _, _, _) = Build(mappedLabel: MappedLabel, server.ClientTransport);

        // The module tool is "demo.count"; the wire tool name must be the bare "count".
        var args = new Dictionary<string, object?> { ["text"] = "one" };
        var result = await handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null);

        Assert.Equal(1, server.Calls);
        Assert.Equal("{\"words\":1}", Text(result));
        Assert.Equal("one", server.LastText);
    }

    [Fact]
    public void BareToolName_StripsLabelPrefix_AndIsVerbatimWithoutDot()
    {
        Assert.Equal("locateComponent", ModuleToolHandler.BareToolName("support.locateComponent"));
        Assert.Equal("count", ModuleToolHandler.BareToolName("demo.count"));
        Assert.Equal("bare", ModuleToolHandler.BareToolName("bare"));
    }

    [Fact]
    public async Task ReservedFlowgentLabel_IsRejected_AndFallsBackToLegacy()
    {
        // An McpServers entry reusing the reserved "flowgent" label must be skipped, so a non-reserved tool under that
        // label is NOT HTTP-routed; it reaches the legacy in-process invoker instead.
        var (handler, _, moduleClient, factory) = Build(mappedLabel: "flowgent");
        moduleClient.InvokeToolAsync("demo", "demo.count", Arg.Any<ToolInvocationRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonNode?>(new JsonObject { ["words"] = 3 }));

        var args = new Dictionary<string, object?> { ["text"] = "a b c" };
        var result = await handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null);

        Assert.Empty(factory.Creates); // no HTTP connect attempted
        await moduleClient.Received(1).InvokeToolAsync("demo", "demo.count", Arg.Any<ToolInvocationRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.Equal(3, JsonNode.Parse(Text(result))!["words"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnmappedLabel_FallsBackToLegacyInProcessInvoker()
    {
        var (handler, _, moduleClient, factory) = Build(mappedLabel: null);
        moduleClient.InvokeToolAsync("demo", "demo.count", Arg.Any<ToolInvocationRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JsonNode?>(new JsonObject { ["words"] = 4 }));

        var args = new Dictionary<string, object?> { ["text"] = "a b c d" };
        var result = await handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null);

        Assert.Empty(factory.Creates);
        await moduleClient.Received(1).InvokeToolAsync("demo", "demo.count", Arg.Any<ToolInvocationRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.Equal(4, JsonNode.Parse(Text(result))!["words"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnmappedLabel_UnknownTool_RaisesNotDeclared()
    {
        var (handler, _, _, _) = Build(mappedLabel: null);

        var ex = await Assert.ThrowsAsync<StepFailedException>(() =>
            handler.InvokeToolAsync(MappedUrl, "ghost", "demo.ghost", null, null, null));

        Assert.Contains("is not declared", ex.Message);
    }

    [Fact]
    public async Task HttpBranch_WrapsBareNumber_SoThePowerFxCellIsNeverADouble()
    {
        // A tool that (incorrectly) declares no output schema and returns a bare number: the handler must wrap it.
        var module = BareNumberModule();
        await using var server = new InProcMcpServer("ping", _ => new InProcMcpServer.ToolReply("5"));
        var (handler, _, _, _) = Build(mappedLabel: "svc", server.ClientTransport, module: module, url: "http://svc");

        var result = await handler.InvokeToolAsync("http://svc", "svc", "svc.ping", null, null, null);

        Assert.Equal("{\"value\":5}", Text(result));
    }

    [Fact]
    public async Task HttpBranch_RunsInputValidation_RejectingAMissingRequiredField()
    {
        await using var server = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{\"words\":0}"));
        var (handler, _, _, _) = Build(mappedLabel: MappedLabel, server.ClientTransport);

        // "text" is a required input on demo.count; omit it.
        var ex = await Assert.ThrowsAsync<StepFailedException>(() =>
            handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", new Dictionary<string, object?>(), null, null));

        Assert.Contains("Input of tool 'demo.count'", ex.Message);
        Assert.Equal(0, server.Calls); // never reached the server
    }

    [Fact]
    public async Task HttpBranch_RunsOutputValidation_RejectingABadShape()
    {
        await using var server = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{\"words\":\"not-an-int\"}"));
        var (handler, _, _, _) = Build(mappedLabel: MappedLabel, server.ClientTransport);

        var args = new Dictionary<string, object?> { ["text"] = "x" };
        var ex = await Assert.ThrowsAsync<StepFailedException>(() =>
            handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null));

        Assert.Contains("Output of tool 'demo.count'", ex.Message);
    }

    [Fact]
    public async Task HttpBranch_IsErrorResult_BecomesStepFailure_WithServerMessage()
    {
        await using var server = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("component missing", IsError: true));
        var (handler, _, _, _) = Build(mappedLabel: MappedLabel, server.ClientTransport);

        var args = new Dictionary<string, object?> { ["text"] = "x" };
        var ex = await Assert.ThrowsAsync<StepFailedException>(() =>
            handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null));

        Assert.Contains("component missing", ex.Message);
    }

    [Fact]
    public async Task HttpBranch_ConnectFailure_BecomesUnavailableStepFailure()
    {
        // No scripted transport → the pool's connect throws, which the handler maps to an "unavailable" step failure.
        var (handler, _, _, _) = Build(mappedLabel: MappedLabel /* no transport queued */);

        var args = new Dictionary<string, object?> { ["text"] = "x" };
        var ex = await Assert.ThrowsAsync<StepFailedException>(() =>
            handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null));

        Assert.Contains($"MCP server '{MappedLabel}' unavailable", ex.Message);
    }

    [Fact]
    public async Task HttpBranch_CallTimeSessionFailure_EvictsAndReconnectsExactlyOnce()
    {
        // The first connect uses a transport whose session is told to fail on send AFTER the initialize handshake,
        // so the first tool call throws a transport error; the single reconnect uses a healthy transport that answers.
        await using var serverA = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{\"words\":0}"));
        await using var serverB = new InProcMcpServer("count", _ => new InProcMcpServer.ToolReply("{\"words\":7}"));
        var failing = new FailableTransport(serverA.ClientTransport);

        var factory = new FakeMcpTransportFactory(failing, serverB.ClientTransport);
        await using var pool = new McpClientPool(factory, NullLoggerFactory.Instance);
        var (handler, _) = Handler(pool, factory, mappedLabel: MappedLabel, module: TestModules.LoadValid(), url: MappedUrl);

        // Warm the first client (handshake succeeds), then flip it to fail on the next send (the tool call).
        await pool.GetAsync(MappedUrl, MappedLabel, CancellationToken.None);
        failing.FailSends = true;

        var args = new Dictionary<string, object?> { ["text"] = "seven" };
        var result = await handler.InvokeToolAsync(MappedUrl, MappedLabel, "demo.count", args, null, null);

        Assert.Equal("{\"words\":7}", Text(result));
        // Exactly two connects: the warm-up (failing) + the single reconnect (healthy).
        Assert.Equal(2, factory.Creates.Count);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static string Text(Microsoft.Extensions.AI.McpServerToolResultContent result) =>
        result.Outputs!.OfType<Microsoft.Extensions.AI.TextContent>().Single().Text!;

    private static (ModuleToolHandler Handler, RunContext Run, IModuleClient ModuleClient, FakeMcpTransportFactory Factory) Build(
        string? mappedLabel,
        ModelContextProtocol.Client.IClientTransport? transport = null,
        ModuleDefinition? module = null,
        string url = MappedUrl)
    {
        var moduleClient = Substitute.For<IModuleClient>();
        module ??= TestModules.LoadValid();

        var factory = transport is null ? new FakeMcpTransportFactory() : new FakeMcpTransportFactory(transport);
        var pool = new McpClientPool(factory, NullLoggerFactory.Instance);

        var engineOptions = new EngineOptions();
        if (mappedLabel is not null)
        {
            engineOptions.McpServers.Add(new McpServerOptions { Label = mappedLabel, Url = url });
        }

        var invoker = new ModuleToolInvoker(moduleClient, Options.Create(engineOptions));
        var run = NewRun(module);
        var handler = new ModuleToolHandler(run, invoker, pool, engineOptions, NullLogger.Instance);
        return (handler, run, moduleClient, factory);
    }

    private static (ModuleToolHandler Handler, RunContext Run) Handler(
        McpClientPool pool, FakeMcpTransportFactory factory, string mappedLabel, ModuleDefinition module, string url)
    {
        var moduleClient = Substitute.For<IModuleClient>();
        var engineOptions = new EngineOptions();
        engineOptions.McpServers.Add(new McpServerOptions { Label = mappedLabel, Url = url });
        var invoker = new ModuleToolInvoker(moduleClient, Options.Create(engineOptions));
        var run = NewRun(module);
        return (new ModuleToolHandler(run, invoker, pool, engineOptions, NullLogger.Instance), run);
    }

    private static RunContext NewRun(ModuleDefinition module)
    {
        var descriptor = module.Workflows.Count > 0
            ? module.Workflows[0]
            : new WorkflowDescriptor
            {
                Id = "wf",
                Version = 1,
                Name = "WF",
                InputSchema = new SchemaSource(new JsonObject { ["type"] = "object" }, null),
                Yaml = "kind: Workflow",
                File = "workflows/wf.yaml",
                Graph = new WorkflowGraphModel("t", [new WorkflowGraphNode("t", "t", "terminal", "success")], []),
            };
        var input = new JsonObject { ["text"] = "hello world" };
        return new RunContext(Guid.NewGuid(), module, descriptor, input, null, CancellationToken.None);
    }

    /// <summary>A module whose single tool declares NO output schema and returns a bare number (exercises the wrapper).</summary>
    private static ModuleDefinition BareNumberModule()
    {
        const string manifest = """
            id: svc
            name: Svc
            tools:
              - name: svc.ping
            workflows:
              - id: wf
                file: workflows/wf.yaml
                version: 1
                name: WF
                input:
                  schema: { type: object }
                terminals:
                  - { id: done, outcome: success }
            """;
        const string declarative = """
            kind: Workflow
            trigger:
              kind: OnConversationStart
              id: wf
              actions:
                - kind: SendActivity
                  id: done
                  activity:
                    text:
                      - "ok"
            """;
        var files = new Dictionary<string, string>
        {
            ["module.yaml"] = manifest,
            ["workflows/wf.yaml"] = declarative,
        };
        var package = new ModulePackage("svc", ModuleLoader.ComputeVersion(files), files);
        var result = ModuleLoader.Load(package, new WorkflowProjector());
        return result.Module ?? throw new InvalidOperationException(
            "bare-number module invalid: " + string.Join(" | ", result.Errors.Select(e => e.Message)));
    }
}
