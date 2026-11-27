# Debugging

[← README](../README.md) · [Orchestration](orchestration.md) · [Engine API](engine-api.md)

## HTTP files

Requests go through the gateway (`http://localhost:5080`). Open them in Visual Studio, VS Code (REST Client) or Rider.

| File | Content |
|------|---------|
| [`backend/engine/Flowgent.Engine.Service/engine.http`](../backend/engine/Flowgent.Engine.Service/engine.http) | Every `/api/engine` route: config, modules, workflows, schemas, submit, runs, abort, human input, events, agents, telemetry, inbox |
| [`backend/template/service/template.http`](../backend/template/service/template.http) | Template module summary and a `template-hello` run |
| Each module's `service/<id>.http` | Module requests (e.g. [support](../samples/support/README.md)) |

## Where to look

| Symptom | Check |
|---------|-------|
| A workflow is missing in the dashboard | `GET /api/engine/modules`: status `Loading`/`Unavailable` (module not running or wrong `Url`), or `Invalid` with `errors[]` (file, step, line). The module service log lists the same errors at startup. |
| `503` from the gateway | Problem details name the service that is down. Check its `/health` on its own port. |
| `503` from the intake | The workflow's module is not `Available`. |
| `400` from the intake | `errors[]` lists the JSON pointers that do not match the input schema. |
| A run failed | `GET /api/engine/runs/{id}`: `currentStepId` is the failing step and `detail` the reason; `stepOutputs` shows what earlier steps produced. `GET /api/engine/events?runId={id}` gives the full transition history. |
| A run never starts | `GET /api/engine/inbox?status=queued`; with `SequentialProcessing` on, another run may be running or awaiting input. |
| `No demo response is defined for step '<id>'` | Demo mode is on and the workflow has no demo file, or its demo file has no entry for the step. See [Demo mode](demo-mode.md#declaring-demo-responses). |
| LLM errors with a real provider | `Flowgent:Llm` settings (`ApiKey`, `ModelName`, `Endpoint`); `GET /api/engine/agents/{agentId}/telemetry` shows the last call and its error. |

## Traces and logs

Under the AppHost, the Aspire dashboard (http://localhost:15080) shows the logs of every service and a trace per request: gateway → engine → module tools, with one span per run step (`step <id>`, tags `flowgent.run_id`, `flowgent.module_id`, `flowgent.workflow_id`, `flowgent.step_id`, `flowgent.step_kind`) and the LLM calls.

For more detail, raise the log level of the `Flowgent` category in the service's `appsettings.Development.json`:

```json
{ "Logging": { "LogLevel": { "Flowgent": "Debug" } } }
```

## Validating definitions offline

Module tests call `ModuleDefinitionAssert.IsValid(path)`, which applies the engine's validation and lists every error with file, line and step:

```bash
dotnet test --project backend/template/tests
```

The YAML JSON Schemas in `backend/shared/Flowgent.Definitions/Schemas/` give editor validation and autocomplete while you type.
