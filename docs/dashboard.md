# Dashboard

[← README](../README.md) · [Engine API](engine-api.md) · [Human input](human-input.md)

`dashboard/` is a generic React console for every module. It contains no step names or module-specific code: pages, graph, forms, filters and badges come from the workflow definitions and run statuses returned by the engine.

## Running it

```bash
cd dashboard
npm install
npm run dev        # http://localhost:5173
```

The AppHost starts it automatically on port 5173. The dev server proxies `/api` and `/flowgent.RunMonitor` to the gateway: `GATEWAY_URL` (set by the AppHost) or `http://localhost:5080`. The dashboard never calls the engine or a module directly.

| Command | Purpose |
|---------|---------|
| `npm run dev` | Dev server |
| `npm run build` | Type check and production build (`dist/`) |
| `npm run typecheck` | `tsc --noEmit` |
| `npm run test -- --run` | Vitest (single run) |
| `npm run generate` | Regenerate `src/gen/` from `backend/protos/run_monitor.proto` (buf) |

Node.js 20.19 or later is required.

## Pages

| Page | Route | Content |
|------|-------|---------|
| Overview | `/` | Module/workflow picker, LLM mode badge, connection status, counters (runs, active agents, awaiting approval, failures), runs in progress with **Abort**, pipeline graph and submission form. |
| Inbox | `/inbox` | Inbox messages filtered by status (queued, processed, failed) with their payload. |
| Runs | `/runs` | Runs of the selected workflow, filterable by status and by step. |
| Approvals | `/approvals` | Pending human inputs of every module with **Approve**, **Reject** (optional reason) and **Abort**. |
| Agents | `/agents` | Agents of every module with Idle/Working status and LLM telemetry (tokens, latency, estimated cost). |
| Event Log | `/events` | Transition events grouped by run, with step names from the workflow graph. |

## Module and workflow selection

`ModuleWorkflowPicker` lists the modules and workflows from `/api/engine/modules` and `/api/engine/workflows` (refreshed every 10 s). It shows the module status (`Loading`, `Available`, `Invalid`, `Unavailable`) and its validation errors. The selection is stored in `localStorage` (`flowgent.selection`); by default the first available workflow is selected.

## Pipeline graph

`PipelineVisualizer` draws `workflow.graph` with React Flow, laid out left-to-right by dagre. Nodes are coloured by the state of the active runs of the selected workflow; terminal nodes have a border coloured by outcome (success or failure); edge labels show the branch conditions.

## Submission form

`RunSubmitForm` builds its fields from the workflow input schema (`string` → input, `x-multiline` → textarea, `enum` → select, `boolean` → checkbox, `number`/`integer` → number) and offers the workflow's samples, grouped by `group`. It validates required fields locally and posts to `/api/engine/workflows/{id}/runs`; the engine validates the same schema and its `400` message is shown. Samples marked `expectInvalid` demonstrate the validation.

## Real-time updates

The gRPC-Web client (`@connectrpc/connect-web`) subscribes to `RunMonitor.Subscribe` through the gateway and reconnects with exponential backoff (2 s up to 30 s), reloading runs after a reconnection. REST polling complements the stream: agents every 3 s, human inputs every 5 s, workflow catalog every 10 s.

## Status badges

| Run status | Colour |
|------------|--------|
| `Queued`, `Running` | Blue |
| `AwaitingInput` | Amber |
| `Completed` | Emerald |
| `Failed`, `Cancelled` | Red |

## Code map

```
dashboard/src/
├── api/          # client.ts (REST, engine routes), grpc-client.ts (RunMonitor stream)
├── gen/          # generated from run_monitor.proto
├── hooks/        # WorkflowCatalogContext, GrpcStreamContext, useRuns, useEvents, useInbox, useAgents, useHumanInputs, useConfig, useRunSubmit
├── components/   # ModuleWorkflowPicker, PipelineVisualizer, RunSubmitForm, RunsList, EventLog, AgentMonitor, LlmModeBadge, layout/
├── pages/        # Overview, Inbox, Runs, Approvals, Agents, Event Log
├── utils/        # badges, graphLayout (dagre), schemaForm, runs
├── types/        # shared types
└── __tests__/    # Vitest + fast-check
```
