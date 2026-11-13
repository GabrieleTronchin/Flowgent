# Module authoring

[← README](../README.md) · [Workflow reference](workflow-reference.md) · [Template module](../modules/_template/README.md)

A module is one business domain: its workflow definitions plus a small ASP.NET Core service built on `Flowgent.Module.Sdk`. Adding a module needs no change to the engine, the gateway code or the dashboard.

Start from [`modules/_template`](../modules/_template/README.md): it is a minimal, valid module (a MAF declarative workflow that calls an LLM, invokes one tool and ends; one tool; one sample; tests) and its README lists the copy-and-rename steps.

Workflows are **Microsoft Agent Framework declarative** documents (`kind: Workflow`, a `trigger` with ordered `actions`, [PowerFx](expressions.md) expressions). See the [Workflow reference](workflow-reference.md) for the action kinds. The engine trial-builds each workflow with the declarative builder; there is no custom step format.

## Layout

```
modules/<id>/
├── definition/           # published to the engine (GET /_module/package)
│   ├── module.yaml
│   ├── workflows/
│   ├── prompts/
│   ├── schemas/
│   └── samples/
├── service/              # Flowgent.Modules.<Name>.csproj: SDK host, tools, domain endpoints, <id>.http
├── tests/                # Flowgent.Modules.<Name>.Tests.csproj
├── docs/ and README.md   # module documentation
└── ...                   # module assets (e.g. sample data)
```

Everything specific to the module stays in its folder: prompts, fixtures, `.http` requests, documentation.

## The service

```csharp
public static class NotesHost
{
    public const string ModuleId = "notes";

    public static WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);                      // tests add configuration and handlers here

        builder.AddServiceDefaults();                    // health, OpenTelemetry, discovery, resilience
        builder.AddFlowgentModule();                     // definition provider, startup validation, IEngineClient
        builder.Services.AddModuleTool<WordCountTool>("notes.wordCount");

        var app = builder.Build();
        app.MapFlowgentModule();                         // /_module/package, /_module/tools/{name}, /api/modules/notes
        app.MapDefaultEndpoints();                       // /health, /alive
        return app;
    }
}
```

`Program.cs` is `NotesHost.Create(new() { Args = args }).Run();`. The `Create` method is what lets tests start the module in-process.

### Configuration (`Flowgent:Module`)

| Key | Default | Description |
|-----|---------|-------------|
| `DefinitionPath` | `../definition` | Definition folder, absolute or relative to the content root. |
| `EngineUrl` | `http://localhost:5081` | Engine address for `IEngineClient` (the AppHost sets `http://engine`). |
| `FailOnInvalidDefinition` | `false` | Stop the service when the definition is invalid. By default it starts anyway and logs the errors, so the engine and the dashboard can show them. |

At startup the SDK validates the module envelope (`module.yaml`: agents, tool/sample references, schema files) and logs an error for every tool declared in `module.yaml` without a registered handler. Each workflow is not step-validated at load; the engine trial-builds it with the declarative builder and reports a build failure as a module error.

## Tools

A tool is C# code the workflow runs over MCP. Declare it in `module.yaml` with its input and output schemas, then implement it:

```csharp
public sealed record WordCountInput(string Text);
public sealed record WordCountOutput(int Words);

public sealed class WordCountTool : ModuleTool<WordCountInput, WordCountOutput>
{
    protected override Task<WordCountOutput> InvokeAsync(WordCountInput input, ToolInvocation invocation, CancellationToken ct) =>
        Task.FromResult(new WordCountOutput(input.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));
}
```

- `ModuleTool<TInput, TOutput>` (de)serializes camelCase JSON; implement `IModuleTool` for raw `JsonNode` access.
- `ToolInvocation` carries `RunId`, `ModuleId`, `WorkflowId`, `StepId`, the input and `Module` (definition root, module root and `config`).
- Throw `ToolException` for expected failures.
- Tools are resolved from DI as transient services, so constructor injection works.

### Calling a tool from a workflow

A workflow invokes a tool with the declarative `InvokeMcpTool` action, where `serverLabel` is the module id and `toolName` is the declared tool name. The engine's MCP tool handler routes the call to the module service (today over the SDK's `/_module/tools` endpoint) and returns the result as a JSON string, read back with `First(Local.x).<field>`:

```yaml
- kind: InvokeMcpTool
  id: count_words
  serverUrl: inproc://notes
  serverLabel: notes            # module id
  toolName: notes.wordCount     # declared tool name
  requireApproval: false
  arguments: { text: =Local.summary }
  output: { result: Local.wc }  # read as =First(Local.wc).words
```

> Tool results must be a non-numeric JSON string or object — a bare top-level number crashes the PowerFx binding. The handler wraps a bare number automatically, but prefer returning an object.

The engine also exposes two reserved built-in tools under `serverLabel: flowgent`, since PowerFx has no `ParseJSON`: `flowgent.input` returns the run's intake input as a record, and `flowgent.parse` turns a JSON string (e.g. an agent's structured answer) into a record — both read with `First(Local.x).<field>`. See [`modules/_template/definition/workflows/template-hello.yaml`](../modules/_template/definition/workflows/template-hello.yaml) for all three in use.

## Domain endpoints

A module can expose its own API (for example a legacy URL) and forward to the engine intake with `IEngineClient`:

```csharp
app.MapPost("/api/modules/notes/submit", async (NoteRequest note, IEngineClient engine, CancellationToken ct) =>
    await engine.SubmitAsync("notes-summary", new { text = note.Text }, ct) switch
    {
        EngineSubmitResult.Accepted a => Results.Accepted(EngineRoutes.Run(a.Ids.RunId), a.Ids),
        EngineSubmitResult.Invalid i => Results.BadRequest(new { error = i.Message }),
        _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
    });
```

The gateway routes `/api/modules/<id>/**` to the module automatically; other public paths need an extra route (see [Gateway](gateway.md)). `IEngineClient` does not retry, so a request never creates a duplicate run.

## Registering the module

With the AppHost (recommended):

1. Add the service and test projects to `backend/Flowgent.slnx`.
2. Add a `ProjectReference` to the service in `backend/src/AppHost/Flowgent.AppHost/Flowgent.AppHost.csproj`.
3. In `AppHost.cs`: `modules.Add<Projects.Flowgent_Modules_Notes>("notes");` (extra gateway routes as further arguments). The module is enabled unless `AppHost:Modules:notes:Enabled` is `false`.

`ModuleWiring` then configures the engine (`Flowgent:Modules`), the gateway (`Gateway:Modules`) and the module (`Flowgent:Module:EngineUrl`).

Without the AppHost, add the module to the engine and gateway `appsettings.json`:

```jsonc
// backend/src/Services/Flowgent.Engine.Service/appsettings.json
"Flowgent": { "Modules": [ { "Id": "notes", "Url": "http://localhost:5084" } ] }

// backend/src/Services/Flowgent.Gateway/appsettings.json
"Gateway": { "Modules": [ { "Id": "notes", "Address": "http://localhost:5084" } ] }
```

Engine options per module: `Enabled` (true), `RefreshSeconds` (5), `TimeoutSeconds` (30), `Retries` (2, transient failures only), `RetryDelayMilliseconds` (500).

## Changing a definition

The service reads its definition folder once at startup. After editing YAML, prompts or schemas, restart the module service: it publishes a new version, the engine reloads it within `RefreshSeconds`, and runs already in progress finish with the version they started with. Bump the workflow `version` when its behaviour changes.

## Testing

- **Definition**: `ModuleDefinitionAssert.IsValid(path)` loads the folder with the engine's rules and throws with every error (file, line, step).
- **End-to-end**: `Flowgent.Testing.PlatformHarness` starts the engine, the module and optionally the gateway in-process on `TestServer`, in Demo mode:

```csharp
await using var platform = await PlatformHarness.StartAsync(options => options
    .AddModule(NotesHost.ModuleId, NotesHost.Create, Path.Combine("modules", "notes", "service"))
    .WithGateway());

var accepted = await platform.SubmitAsync("notes-summary", new { text = "Hello" });
var run = await platform.WaitForTerminalAsync(accepted.RunId);
Assert.Equal(RunStatus.Completed, run.Status);
```

The harness also offers `WaitForHumanInputAsync`, `ApproveAsync`, `RejectAsync`, `AbortAsync`, `GetEventsAsync`, `StopModuleAsync`, `StartModuleAsync` and `RestartModuleAsync`. See `modules/_template/tests/TemplateModuleTests.cs` for a complete example.

Run them with `dotnet test --project modules/<id>/tests` or the whole solution with `dotnet test --solution backend/Flowgent.slnx`.
