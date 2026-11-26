namespace Flowgent.Fixtures.Module;

using System.Text.Json.Nodes;
using Flowgent.Module.Sdk;

/// <summary>
/// Test-only module service: the <c>fixture.reverse</c> and <c>fixture.echo</c> tools. <c>fixture.echo</c> stands in
/// for the "external API" the pre-migration <c>http</c> step called; the migration proved tool calls (InvokeMcpTool)
/// but not a declarative HTTP action, so the echo is reached as a module tool. The legacy
/// <c>POST /api/modules/fixture/echo</c> endpoint is kept for parity but is no longer called by a workflow.
/// </summary>
public static class FixtureHost
{
    public const string ModuleId = "fixture";
    public const string FlowWorkflowId = "fixture-flow";
    public const string RulesWorkflowId = "fixture-rules";
    public const string RunHeader = "X-Fixture-Run";

    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();
        builder.AddFlowgentModule();
        builder.Services.AddModuleTool<ReverseTool>(ReverseTool.Name);
        builder.Services.AddModuleTool<EchoTool>(EchoTool.Name);

        var app = builder.Build();

        app.MapFlowgentModule();
        app.MapPost($"/api/modules/{ModuleId}/echo", (JsonObject body, HttpRequest request) => Results.Ok(new JsonObject
        {
            ["echoed"] = body["value"]?.DeepClone(),
            ["queue"] = body["queue"]?.DeepClone(),
            ["runHeader"] = request.Headers[RunHeader].ToString(),
        }));
        app.MapDefaultEndpoints();

        return app;
    }
}

public sealed record ReverseInput(string Text);

public sealed record ReverseOutput(string Reversed);

/// <summary><c>fixture.reverse</c>: returns the text reversed.</summary>
public sealed class ReverseTool : ModuleTool<ReverseInput, ReverseOutput>
{
    public const string Name = "fixture.reverse";

    protected override Task<ReverseOutput> InvokeAsync(ReverseInput input, ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(new ReverseOutput(new string([.. input.Text.Reverse()])));
}

public sealed record EchoInput(string Value, string Queue);

public sealed record EchoOutput(string Echoed, string Queue, string RunHeader);

/// <summary>
/// <c>fixture.echo</c>: echoes the value/queue back with the run id as <c>runHeader</c>, standing in for the external
/// echo API the pre-migration <c>http</c> step called (which echoed the <c>X-Fixture-Run</c> header = the run id).
/// </summary>
public sealed class EchoTool : ModuleTool<EchoInput, EchoOutput>
{
    public const string Name = "fixture.echo";

    protected override Task<EchoOutput> InvokeAsync(EchoInput input, ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(new EchoOutput(input.Value, input.Queue, invocation.RunId.ToString()));
}
