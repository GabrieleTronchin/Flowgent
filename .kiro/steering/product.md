---
inclusion: always
---

# Product Overview

Flowgent is a reusable base for building LLM workflows. Workflows are defined in YAML and executed by a domain-agnostic engine on Microsoft Agent Framework Workflows. Each business domain is a module service that owns its definitions and custom code. A generic dashboard monitors every module through a single gateway, and a .NET Aspire AppHost starts everything with one command.

## Components

- **Gateway** (`Flowgent.Gateway`, :5080): the only entry point for the dashboard and clients. Config-driven YARP routes: `/api/engine/**` and the gRPC-Web stream `/flowgent.RunMonitor/**` to the engine, `/api/modules/{id}/**` (plus per-module extra routes) to module services. Correlation id, `503` problem details when a target is down.
- **Engine** (`Flowgent.Engine.Service`, :5081): pulls module packages, validates and compiles workflows, runs them. Owns runs, transition events, the inbox, human input, LLM access (Demo default, OpenAI-compatible), telemetry, the `/api/engine` API and the gRPC `RunMonitor` stream. Contains no domain code.
- **Module services** (`backend/modules/<id>/service`, on `Flowgent.Module.Sdk`): publish `definition/` at `GET /_module/package`, expose domain endpoints and submit inputs through `IEngineClient`. Domain tools are served by **MCP servers** (Streamable HTTP), reached by the engine via `Flowgent:McpServers[]` (label → URL); the support demo's `locateComponent` ships as the standalone `Flowgent.Mcp.Support` server. The legacy in-process `POST /_module/tools/{name}` path remains as the fallback for labels not mapped to an MCP server.
- **Dashboard** (`dashboard/`, :5173): generic React console; graph, forms, filters and badges come from the workflow definitions.
- **AppHost** (`Flowgent.AppHost`): starts engine, enabled modules, gateway and dashboard; Aspire dashboard at :15080.

## Workflow model

- A module has `module.yaml` (id, config, agents, tools, workflow descriptors and sample files), `workflows/*.yaml`, `prompts/`, `schemas/` and `samples/`.
- A workflow is a **Microsoft Agent Framework declarative** document (`kind: Workflow`, a `trigger` with ordered `actions`) referenced by a descriptor in `module.yaml` (id, version, input JSON Schema, title template, `terminals` with a `success` or `failure` outcome). Actions in use: `InvokeAzureAgent` (LLM step, resolved by agent name through the engine's `IChatClient`), `InvokeMcpTool` (domain tool), `SetVariable`, `ConditionGroup` and `GotoAction` (branching), `Question` (human approval), `SendActivity` and `EndConversation` (terminals).
- Expressions are PowerFx (`=Expr`); results are read with `First(Local.x).<field>`. Two built-in tools under `serverLabel: flowgent` (`flowgent.input`, `flowgent.parse`) cover the JSON parsing PowerFx lacks.
- Tools are reached by `serverLabel`: a label mapped in `Flowgent:McpServers[]` goes to a standalone MCP server over Streamable HTTP; any other label uses the module service's `POST /_module/tools/{name}`.
- Definitions are validated at load time (module envelope JSON Schema, references, samples, and a trial build with the declarative builder); invalid modules are reported with file and message and not loaded.

## Runs

- Intake: `POST /api/engine/workflows/{id}/runs` validates the input and stores an inbox message plus a `Queued` run in one operation, returning `202 { messageId, runId }`.
- `InboxProcessor` starts runs FIFO without awaiting them (no retries, start errors recorded on the message); `SequentialProcessing` starts one run at a time.
- `RunStatus`: `Queued`, `Running`, `AwaitingInput`, `Completed`, `Failed`, `Cancelled` (last three terminal). Each run has a current step id.
- Every transition updates the run and appends an event in one `SaveChanges` (dual-write), then is published on the gRPC stream.
- Human input: `GET /api/engine/human-inputs`, `POST .../{runId}/approve|reject` (`204`/`404`/`409`); abort `POST /api/engine/runs/{runId}/abort` ends the run `Failed` with `Aborted by user`.
- Demo mode (default): agent steps answer from the demo file each module declares per workflow (`workflows[].demo`, e.g. `demo/<workflow>.yaml`, keyed by step id); no API key needed. The engine holds no workflow-specific demo logic.

## Modules in the repository

- `backend/modules/support`: reference module and regression suite (support-email triage with human approval). Domain details live in `backend/modules/support/README.md` and `backend/modules/support/docs/`; do not describe them in platform documentation.
- `backend/modules/support/mcp/Flowgent.Mcp.Support`: standalone MCP server that serves support's `locateComponent` tool (example of a domain MCP server).
- `backend/modules/jokes`: tiny example module (one agent step), enabled in the AppHost.
- `backend/modules/_template`: minimal module to copy (`template-hello`), disabled in the AppHost by default.
- `backend/tests/Fixtures/Flowgent.Fixtures.Module`: test-only module used by the integration tests (agent, tool, routing and human-approval flows).

## Container images

`deploy/` has four Dockerfiles (engine, gateway, dashboard, support-mcp; the last is an example module, not core) and `.github/workflows/images.yml`, which publishes them to GHCR on `v*` tags (a floating `edge` tag on master). The first release, `v0.1.0-alpha.1`, is not tagged yet. Product maturity is **beta**; the release channel is **alpha**. There is no `docker compose` yet, and the dashboard image does not proxy to the gateway yet.

## Documentation

Platform docs are in `docs/` (architecture, workflow reference, expressions, module authoring, gateway, engine API, demo mode, inbox, human input, dashboard, orchestration, debugging). Platform docs use generic or template examples; module docs live in the module folder.

## Constraints

- The engine and gateway never reference module projects or domain types; enforced by `Flowgent.Architecture.Tests`.
- Module services reference only the SDK, contracts, definitions and service defaults.
- Expected failures use result types (`IntakeResult`, `EngineSubmitResult`, `HumanInputResult`, step failures with descriptive reasons); exceptions are for unexpected errors.
- No authentication: the gateway is the only intended public entry point. Secrets (`Flowgent:Llm:ApiKey`) come from configuration and are never returned by any endpoint.
