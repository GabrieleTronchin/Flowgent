# Support endpoints

[← Support module](../README.md) · [Scenarios](scenarios.md) · [Platform: Engine API](../../../docs/engine-api.md)

All requests go through the gateway (`http://localhost:5080`). The gateway routes `/api/modules/support/**` and `/api/support/**` to the support service (port 5082).

## Submit a support email

`POST /api/support/emails` (the original URL, kept for compatibility) or `POST /api/modules/support/emails`.

```http
POST http://localhost:5080/api/support/emails
Content-Type: application/json

{
  "sender": "dev.team@company.com",
  "subject": "NullReferenceException in OrderController - Application A",
  "body": "We are seeing a NullReferenceException in Application A when calling the GetOrderSummary endpoint..."
}
```

Property names are case-insensitive (`Subject` works too). The service validates the email and forwards `{ sender, subject, body }` to the engine intake for workflow `support-email`.

| Status | Body | When |
|--------|------|------|
| `202 Accepted` | `{ "messageId": "...", "runId": "..." }`, `Location: /api/engine/runs/{runId}` | Email accepted; the run is queued |
| `400 Bad Request` | `{ "error": "Subject and Body are required." }` | Subject or body missing or blank |
| `503 Service Unavailable` | `{ "error": "..." }` | The engine is down, or the workflow is not available yet |
| `503` (problem details) | `{ "title": "Service unavailable", "service": "support", ... }` | Returned by the gateway when the support service is down |

## Following a run

The support module has no read endpoints of its own: runs, approvals, events and agents come from the generic [engine API](../../../docs/engine-api.md).

| Need | Request |
|------|---------|
| The run | `GET /api/engine/runs/{runId}` |
| Support runs | `GET /api/engine/runs?workflowId=support-email` |
| Runs awaiting approval | `GET /api/engine/human-inputs?workflowId=support-email` |
| Approve / reject | `POST /api/engine/human-inputs/{runId}/approve`, `POST /api/engine/human-inputs/{runId}/reject` with `{ "reason": "..." }` |
| Abort | `POST /api/engine/runs/{runId}/abort` |
| Event log | `GET /api/engine/events?moduleId=support` |
| Agents | `GET /api/engine/agents?moduleId=support` |

The read routes of the original application (`/api/support/issues`, `/api/support/events`, `/api/support/agents`, `/api/support/approvals/...`) were replaced by these engine routes.

## Module SDK endpoints

| Route | Through the gateway | Purpose |
|-------|---------------------|---------|
| `GET /api/modules/support` | yes | Module summary: id, name, description, version, valid, workflows |
| `GET /_module/package` | no (engine only) | Definition files and version |
| `POST /_module/tools/{name}` | no (engine only) | Generic SDK tool route; the support module registers no tools. `locateComponent` runs on the standalone MCP server `Flowgent.Mcp.Support`, mapped by the engine through `Flowgent:McpServers[]` (label `support`). |
| `GET /health`, `GET /alive` | no | Health checks on port 5082 |

## `support.http`

[`service/support.http`](../service/support.http) contains one request per scenario (A1–A3, B1–B3, Out-of-Scope, Ambiguous Routing, Failed Routing, Empty Input) plus the run, approval, event and agent queries above. It is generated from [`scenarios.yaml`](../definition/samples/scenarios.yaml); do not edit it by hand (see [Scenarios](scenarios.md#adding-a-scenario)). Start the platform with the AppHost and send the requests from Visual Studio, VS Code (REST Client) or Rider.
