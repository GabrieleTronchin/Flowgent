# Writing modules

[← README](../../README.md) · [Documentation](../README.md) · [Writing workflows](writing-workflows.md) · [Deployment](deployment.md)

A **module** is one use case: a folder of definition files (workflows, prompts, schemas, samples, demo answers)
and, when its workflows call domain tools, an MCP server that serves them. Adding a module needs no change to the
engine, the gateway or the dashboard.

## A complete module

This module, `notes`, summarises a text with one agent step. It is only YAML and JSON, so it runs on the stock
images.

```
notes/
├── module.yaml
├── workflows/notes-summary.yaml
├── schemas/notes-summary.input.schema.json
├── demo/notes-summary.yaml          # optional: Demo-mode answers
└── samples/notes.samples.yaml       # optional: inputs offered by the dashboard
```

`module.yaml` declares the module, its agents and the workflow descriptor:

```yaml
id: notes
name: Notes
description: Summarises a text.
agents:
  - id: writer
    name: Writer
    group: Writers
    role: Writer
    persona: A concise writer.
workflows:
  - id: notes-summary
    file: workflows/notes-summary.yaml
    version: 1
    name: Summarise a note
    title: "{text}"
    input:
      schema: { file: schemas/notes-summary.input.schema.json }
    demo: demo/notes-summary.yaml
    terminals:
      - id: done
        outcome: success
samples:
  - samples/notes.samples.yaml
```

`schemas/notes-summary.input.schema.json` is the input the dashboard form and the intake use:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["text"],
  "properties": { "text": { "type": "string", "title": "Text", "minLength": 1 } }
}
```

`workflows/notes-summary.yaml` reads the input, asks the agent, parses its JSON answer and ends:

```yaml
kind: Workflow
trigger:
  kind: OnConversationStart
  id: notes-summary
  actions:
    # The run input as a record.
    - kind: InvokeMcpTool
      id: read_input
      serverUrl: inproc://flowgent
      serverLabel: flowgent
      toolName: flowgent.input
      requireApproval: false
      arguments:
        payload: =System.LastMessage.Text
      output:
        result: Local.intake

    # Agent step "summarize", answered by agent "writer".
    - kind: InvokeAzureAgent
      id: summarize
      conversationId: =System.ConversationId
      agent:
        name: summarize|writer
      input:
        messages: =UserMessage(Concatenate("Text: ", First(Local.intake).text))
      output:
        messages: Local.summarize

    # The agent's JSON answer as a record.
    - kind: InvokeMcpTool
      id: parse_summary
      serverUrl: inproc://flowgent
      serverLabel: flowgent
      toolName: flowgent.parse
      requireApproval: false
      arguments:
        payload: =MessageText(Local.summarize)
      output:
        result: Local.summaryObj

    # The step's detail, then its output: this makes "summarize" a step of the run.
    - kind: SetVariable
      id: summarize_detail
      variable: Global.summarize__detail
      value: =First(Local.summaryObj).summary
    - kind: SetVariable
      id: summarize_snapshot
      variable: Global.summarize
      value: =MessageText(Local.summarize)

    # Terminal "done": final output, message and terminal id.
    - kind: SetVariable
      id: done_final
      variable: Global.__finalOutput
      value: =MessageText(Local.summarize)
    - kind: SetVariable
      id: done_message
      variable: Global.__message
      value: =First(Local.summaryObj).summary
    - kind: SetVariable
      id: done_id
      variable: Global.__terminalId
      value: ="done"
    - kind: SendActivity
      id: done
      activity:
        text:
          - "{Global.__message}"
```

`demo/notes-summary.yaml` answers the agent step when there is no model:

```yaml
steps:
  summarize:
    extract:
      text: { field: Text }
    output:
      summary: "[Demo] {text}"
```

`samples/notes.samples.yaml` gives the dashboard a ready input:

```yaml
samples:
  - id: notes-short
    name: A short note
    workflow: notes-summary
    input: { text: Buy milk and call the bank. }
```

The conventions in the workflow (`flowgent.input`, `flowgent.parse`, `Global.<step>`, the terminal variables) are
explained in [Writing workflows](writing-workflows.md#workflow-definition-maf-declarative). Write workflow files in
block style: the declarative reader does not accept inline mappings (`{ key: value }`). The editor can validate
the YAML files with the JSON Schemas served by `GET /api/engine/schemas/{module|samples|demo}`.

## Run it

Put the folder under `compose/definitions/notes/` and register it (see
[Docker Compose](../../compose/README.md#your-own-module)), or mount it into the engine image anywhere else
([Deployment](deployment.md#your-modules)). The engine reports definition errors with file and message in
`GET /api/engine/modules` and in the dashboard; a module with errors is not loaded.

Edit the files and the engine reloads them within a few seconds. Runs in progress keep the version they started
with; bump the workflow `version` when its behaviour changes.

## Domain tools on an MCP server

When a workflow needs your own logic (read a database, call an API, search files), serve it as a tool from an
MCP server over Streamable HTTP. Any language and any MCP SDK work.

1. Declare the tool in `module.yaml` with its qualified name (`<module id>.<tool>`) and its input and output
   schemas. The engine validates both.

   ```yaml
   tools:
     - name: notes.wordCount
       description: Counts the words of a text.
       input:
         schema: { type: object, required: [text], properties: { text: { type: string } } }
       output:
         schema: { type: object, required: [words], properties: { words: { type: integer } } }
       timeoutSeconds: 10
   ```

2. Serve it from the MCP server under its **bare** name (`wordCount`): the engine strips the `<label>.` prefix on
   the wire. Return one JSON object as text (camelCase, not a bare number) and report expected failures as an
   MCP error result (`isError`).
3. Call it from the workflow with `InvokeMcpTool`; `serverLabel` names the server:

   ```yaml
   - kind: InvokeMcpTool
     id: count_words
     serverUrl: inproc://notes
     serverLabel: notes
     toolName: notes.wordCount
     requireApproval: false
     arguments:
       text: =First(Local.summaryObj).summary
     output:
       result: Local.wc                 # read as =First(Local.wc).words
   ```

4. Map the label to the server on the engine: `Flowgent__McpServers__0__Label=notes` and
   `Flowgent__McpServers__0__Url=http://notes-mcp:8080`.

A failed call reconnects once, then the step fails with `MCP server '<label>' unavailable`. There is no
authentication: keep MCP servers on a private network and credentials out of the URL. A .NET example of an MCP
server is in [Examples](../examples.md).

## Demo answers

In Demo mode each agent step answers from the module's demo file (`workflows[].demo`). The file is optional; a
step without an answer fails in Demo mode with a message saying where to add it. Format and rules:
[Demo mode](demo-mode.md#declaring-demo-responses).

## Advanced: a module service with the Module SDK

A module can also be a .NET service built on `Flowgent.Module.Sdk`. The service publishes its definition folder at
`GET /_module/package` (the engine pulls it when the folder is not on its volume), can host tools in-process
(`POST /_module/tools/{name}`) and can expose its own endpoints. Most modules do not need it: prefer a definition
folder and an MCP server. The module template, [`backend/template`](../../backend/template/README.md), is such a
service, with tests; its README lists the copy-and-rename steps.

### The service

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

`Program.cs` is `NotesHost.Create(new() { Args = args }).Run();`. The `Create` method lets tests start the module
in-process. Settings (`Flowgent:Module:*`): [Configuration](../reference/configuration.md#module-services-module-sdk-advanced).
At startup the SDK validates the definition and logs an error for every declared tool without a handler.

### In-process tools

A label that is not mapped in `Flowgent:McpServers[]` goes to the module service's `/_module/tools/{name}`:

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
- `ToolInvocation` carries `RunId`, `ModuleId`, `WorkflowId`, `StepId`, the input and `Module` (definition root,
  module root and `config`).
- Throw `ToolException` for expected failures. Tools are transient DI services, so constructor injection works.

### Domain endpoints

A module can expose its own API and forward to the engine intake with `IEngineClient`:

```csharp
app.MapPost("/api/modules/notes/submit", async (NoteRequest note, IEngineClient engine, CancellationToken ct) =>
    await engine.SubmitAsync("notes-summary", new { text = note.Text }, ct) switch
    {
        EngineSubmitResult.Accepted a => Results.Accepted(EngineRoutes.Run(a.Ids.RunId), a.Ids),
        EngineSubmitResult.Invalid i => Results.BadRequest(new { error = i.Message }),
        _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
    });
```

Register the module on the gateway too (`Gateway__Modules__<n>__Id/Address`) so `/api/modules/<id>/**` reaches it;
other public paths need `ExtraRoutes`. `IEngineClient` does not retry, so a request never creates a duplicate run.

### Registering and testing

- Engine: `Flowgent__Modules__<n>__Id` and `__Url` (the service address). In this repository the samples AppHost
  does it with `platform.AddModule<Projects.Flowgent_Modules_Notes>("notes")`; see
  [Orchestration](../contributing/orchestration.md).
- The service reads its definition folder once at startup: restart it after editing the files.
- `ModuleDefinitionAssert.IsValid(path)` loads a folder with the engine's rules and throws with every error.
- `Flowgent.Testing.PlatformHarness` starts the engine, the module and optionally the gateway in-process, in Demo
  mode:

```csharp
await using var platform = await PlatformHarness.StartAsync(options => options
    .AddModule(NotesHost.ModuleId, NotesHost.Create, notesServiceFolder) // absolute path, or relative to backend/
    .WithGateway());

var accepted = await platform.SubmitAsync("notes-summary", new { text = "Hello" });
var run = await platform.WaitForTerminalAsync(accepted.RunId);
Assert.Equal(RunStatus.Completed, run.Status);
```

The harness also offers `WaitForHumanInputAsync`, `ApproveAsync`, `RejectAsync`, `AbortAsync`, `GetEventsAsync`,
`StopModuleAsync`, `StartModuleAsync` and `RestartModuleAsync`. `backend/template/tests/TemplateModuleTests.cs` is a
complete example.
