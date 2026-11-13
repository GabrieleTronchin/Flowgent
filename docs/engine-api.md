# Engine API

[← README](../README.md) · [Gateway](gateway.md) · [Human input](human-input.md) · [Inbox](inbox.md)

The engine exposes a module-agnostic API under `/api/engine`, reached through the gateway (`http://localhost:5080`). JSON is camelCase with string enums. Errors are problem details (`title`, `detail`, optional `errors`). Ready-made requests: [`engine.http`](../backend/src/Services/Flowgent.Engine.Service/engine.http).

No endpoint returns secrets: API keys and LLM endpoint URLs stay in configuration.

## Modules and workflows

| Method | Route | Response |
|--------|-------|----------|
| GET | `/api/engine/config` | `{ sequentialProcessing, demoMode, llmProvider, llmModel }` |
| GET | `/api/engine/modules` | `[{ id, name, description, status, version, workflows[], errors[], lastLoadedAt, lastError }]`; `status` is `Loading`, `Available`, `Invalid` or `Unavailable`; `errors[]` are `{ moduleId, file, stepId, line, message, severity }` |
| GET | `/api/engine/workflows?moduleId=` | `[{ id, moduleId, name, description, version, available, inputSchema, graph, samples[] }]` |
| GET | `/api/engine/workflows/{workflowId}` | One workflow; `404` when unknown |
| GET | `/api/engine/schemas/{name}` | JSON Schema of a YAML format: `module` or `samples`; `404` otherwise (the custom `workflow` schema was removed with the move to MAF declarative) |

`graph` is `{ start, nodes: [{ id, name, kind, outcome }], edges: [{ from, to, label }] }`, projected from the declarative workflow; `kind` is the action/terminal kind, `outcome` is `success`/`failure` on terminal nodes, `label` is the edge condition. `samples[]` are `{ id, name, group, description, workflowId, input, expectInvalid }`.

## Runs

| Method | Route | Response |
|--------|-------|----------|
| POST | `/api/engine/workflows/{workflowId}/runs` | Body = the input. `202 { messageId, runId }` with `Location: /api/engine/runs/{runId}`; `400` input does not match the input schema; `404` unknown workflow; `503` module unavailable |
| GET | `/api/engine/runs?moduleId=&workflowId=&status=&limit=` | Run summaries, newest first. `limit` 1–1000, default 500 |
| GET | `/api/engine/runs/{runId}` | Run detail; `404` |
| POST | `/api/engine/runs/{runId}/abort` | `202` running run cancelled; `204` queued run failed; `404`; `409` already terminal |

A `400` from the intake carries the manifest's input `errorMessage` (or a default) as `detail` and the schema errors:

```json
{
  "title": "Invalid input",
  "status": 400,
  "detail": "Text is required.",
  "errors": [ { "pointer": "/text", "message": "..." } ]
}
```

Run summary: `{ id, moduleId, workflowId, workflowVersion, title, status, currentStepId, detail, assignedAgentId, createdAt, lastUpdated }`. The detail adds `input`, `stepOutputs` (object keyed by step id) and `finalOutput`.

Abort ends the run in `Failed` with detail `Aborted by user` (a single transition) and releases a pending human input.

## Human input

| Method | Route | Response |
|--------|-------|----------|
| GET | `/api/engine/human-inputs?moduleId=&workflowId=` | `[{ runId, moduleId, workflowId, stepId, runTitle, review: { title, fields: [{ label, value }] }, requestedAt }]` |
| POST | `/api/engine/human-inputs/{runId}/approve` | `204`; `404` unknown run; `409` not awaiting input or already terminal |
| POST | `/api/engine/human-inputs/{runId}/reject` | Body `{ "reason": "..." }` (optional). `204` / `404` / `409` |

See [Human input](human-input.md).

## Monitoring

| Method | Route | Response |
|--------|-------|----------|
| GET | `/api/engine/events?limit=&moduleId=&workflowId=&runId=` | Transition events, newest first, `limit` 1–200 (default 200): `{ id, runId, moduleId, workflowId, kind, previousStepId, stepId, status, timestamp, detail }` |
| GET | `/api/engine/agents?moduleId=` | `[{ agentId, moduleId, name, group, role, status, currentRunId, currentRunTitle, currentStepId }]`; `status` is `Working` while the agent is assigned to a non-terminal run, else `Idle`. `404` when `Flowgent:EnableVisualization` is `false` |
| GET | `/api/engine/agents/{agentId}/telemetry` | `{ agentId, totalPromptTokens, totalCompletionTokens, totalCalls, averageLatencyMs, lastCall }` |
| GET | `/api/engine/telemetry/summary?moduleId=` | `{ totalTokens, totalCalls, averageLatencyMs, errorRate }` |
| GET | `/api/engine/inbox?status=&moduleId=` | `[{ id, moduleId, workflowId, runId, receivedAt, processedAt, error, status, payload }]`; `status` filter: `queued`, `processed`, `failed` |
| GET | `/api/engine/inbox/{messageId}` | One message; `404` |

Telemetry is kept in memory by the engine. `lastCall` includes model, tokens, latency, success, error and the module, workflow, run and step of the call.

## gRPC run stream

`backend/protos/run_monitor.proto`, served as gRPC-Web at `/flowgent.RunMonitor/Subscribe` through the gateway:

```proto
service RunMonitor {
  rpc Subscribe (SubscribeRequest) returns (stream RunUpdate);
}
message SubscribeRequest {        // optional filters; an update must match all that are set
  optional string run_id = 1;
  optional string workflow_id = 2;
  optional string module_id = 3;
}
message RunUpdate {
  string run_id = 1; string module_id = 2; string workflow_id = 3; string step_id = 4;
  string status = 5; string event_kind = 6; string timestamp = 7;
  optional string detail = 8; optional string title = 9;
}
```

Every transition (including `Queued`) is published. The stream returns `Unavailable` when visualization is disabled and `InvalidArgument` when `run_id` is not a GUID. Each subscriber has its own bounded buffer, so a slow client does not slow down the others.

## Engine configuration (`Flowgent`)

| Key | Default | Description |
|-----|---------|-------------|
| `EnableVisualization` | `true` | Enables the gRPC stream and the agents endpoint. |
| `SequentialProcessing` | `false` | Start a run only when no other run is running or awaiting input. See [Inbox](inbox.md). |
| `InboxPollingIntervalSeconds` | `5` (`1` in `appsettings.json`) | Inbox polling interval. |
| `DefaultToolTimeoutSeconds` | `30` | Tool timeout when neither the tool nor the module sets one. |
| `Modules[]` | — | `{ Id, Url, Enabled, RefreshSeconds, TimeoutSeconds, Retries, RetryDelayMilliseconds }`; see [Module authoring](module-authoring.md#registering-the-module). |
| `Llm` | — | LLM provider; see [Demo mode](demo-mode.md). |

