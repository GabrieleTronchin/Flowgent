---
inclusion: always
---

# Product Overview

Flowgent is a reusable base for building LLM workflows. Workflows are defined in YAML and executed by a domain-agnostic engine on Microsoft Agent Framework Workflows. Each business domain is a module service that owns its definitions and custom code. A generic dashboard monitors every module through a single gateway, and a .NET Aspire AppHost starts everything with one command.

## Components

- **Gateway** (`Flowgent.Gateway`, :5080): the only entry point for the dashboard and clients. Config-driven YARP routes: `/api/engine/**` and the gRPC-Web stream `/flowgent.RunMonitor/**` to the engine, `/api/modules/{id}/**` (plus per-module extra routes) to module services. Correlation id, `503` problem details when a target is down.
- **Engine** (`Flowgent.Engine.Service`, :5081): pulls module packages, validates and compiles workflows, runs them. Owns runs, transition events, the inbox, human input, LLM access (Demo default, OpenAI-compatible), telemetry, the `/api/engine` API and the gRPC `RunMonitor` stream. Contains no domain code.
- **Module services** (`modules/<id>/service`, on `Flowgent.Module.Sdk`): publish `definition/` at `GET /_module/package`, expose domain endpoints and submit inputs through `IEngineClient`. Domain tools are served by **MCP servers** (Streamable HTTP), reached by the engine via `Flowgent:McpServers[]` (label → URL); the support demo's `locateComponent` ships as the standalone `Flowgent.Mcp.Support` server. The legacy in-process `POST /_module/tools/{name}` path remains as the fallback for labels not mapped to an MCP server.
- **Dashboard** (`dashboard/`, :5173): generic React console; graph, forms, filters and badges come from the workflow definitions.
- **AppHost** (`Flowgent.AppHost`): starts engine, enabled modules, gateway and dashboard; Aspire dashboard at :15080.

## Workflow model

- A module has `module.yaml` (id, config, agents, tools, workflow and sample files), `workflows/*.yaml`, `prompts/`, `schemas/` and `samples/`.
- A workflow has an id, version, input JSON Schema, a start step and steps. Step kinds: `llm` (structured output validated against a schema, `demo:` response), `rule` (ordered regex/expression rules), `map` (lookups and computed values), `human-input` (approve/reject), `tool` (module service call), `http` (external API), `terminal` (success or failure).
- Transitions: `next` or ordered `transitions` with Scriban `when` conditions (first true wins, no condition = default). Templates use Scriban with `input`, `steps.<id>`, `config`, `run` (and `agent` in llm prompts, `output` in map steps).
- Definitions are validated at load time (format schema, references, reachability, templates, agents, tools, samples); invalid modules are reported with file/step/line and not loaded.

## Runs

- Intake: `POST /api/engine/workflows/{id}/runs` validates the input and stores an inbox message plus a `Queued` run in one operation, returning `202 { messageId, runId }`.
- `InboxProcessor` starts runs FIFO without awaiting them (no retries, start errors recorded on the message); `SequentialProcessing` starts one run at a time.
- `RunStatus`: `Queued`, `Running`, `AwaitingInput`, `Completed`, `Failed`, `Cancelled` (last three terminal). Each run has a current step id.
- Every transition updates the run and appends an event in one `SaveChanges` (dual-write), then is published on the gRPC stream.
- Human input: `GET /api/engine/human-inputs`, `POST .../{runId}/approve|reject` (`204`/`404`/`409`); abort `POST /api/engine/runs/{runId}/abort` ends the run `Failed` with `Aborted by user`.
- Demo mode (default): `llm` steps answer from their YAML `demo:` block; no API key needed.

## Modules in the repository

- `modules/support`: reference module and regression suite (support-email triage with human approval). Domain details live in `modules/support/README.md` and `modules/support/docs/`; do not describe them in platform documentation.
- `modules/_template`: minimal module to copy (`template-hello`), disabled in the AppHost by default.
- `backend/tests/Fixtures/Flowgent.Fixtures.Module`: test-only module covering every step kind.

## Documentation

Platform docs are in `docs/` (architecture, workflow reference, expressions, module authoring, gateway, engine API, demo mode, inbox, human input, dashboard, orchestration, debugging). Platform docs use generic or template examples; module docs live in the module folder.

## Constraints

- The engine and gateway never reference module projects or domain types; enforced by `Flowgent.Architecture.Tests`.
- Module services reference only the SDK, contracts, definitions and service defaults.
- Expected failures use result types (`IntakeResult`, `EngineSubmitResult`, `HumanInputResult`, step failures with descriptive reasons); exceptions are for unexpected errors.
- No authentication: the gateway is the only intended public entry point. Secrets (`Flowgent:Llm:ApiKey`, `Flowgent:Secrets:*`) come from configuration and are never returned by any endpoint.
