# Architecture

[← README](../README.md) · [Workflow reference](workflow-reference.md) · [Engine API](engine-api.md) · [Gateway](gateway.md)

Flowgent is a small distributed platform that runs LLM workflows described in YAML. The engine knows nothing about business domains: every domain lives in a **module service** that owns its definitions and custom code.

## Topology

```mermaid
graph LR
    Browser[Dashboard<br/>Vite :5173] -->|/api/**, gRPC-Web| GW[Gateway<br/>YARP :5080]
    Client[HTTP clients / .http files] --> GW
    GW -->|"/api/engine/**<br/>/flowgent.RunMonitor/**"| ENG[Engine service :5081]
    GW -->|"/api/modules/{id}/**<br/>+ extra routes"| MOD[Module services<br/>support :5082, template :5083]
    ENG -->|"GET /_module/package<br/>POST /_module/tools/{name}"| MOD
    MOD -->|"POST /api/engine/workflows/{id}/runs"| ENG
    ENG -->|IChatClient| LLM[(Demo / OpenAI-compatible)]
    MOD -->|module tools reach| EXT[(External APIs / data)]
    HOST[Aspire AppHost] -. starts and wires .-> GW & ENG & MOD & Browser
```

| Component | Project | Responsibility |
|-----------|---------|----------------|
| Gateway | `Flowgent.Gateway` | Single public entry point. Config-driven YARP routes, gRPC-Web passthrough, correlation id, `503` problem details. See [Gateway](gateway.md). |
| Engine service | `Flowgent.Engine.Service` | Pulls module packages, validates and compiles workflows, runs them on Microsoft Agent Framework Workflows. Owns runs, events, inbox, human input, LLM access (Demo or OpenAI-compatible), telemetry, the `/api/engine` API and the gRPC run stream. |
| Module services | `modules/<id>/service` (on `Flowgent.Module.Sdk`) | Own one domain: `module.yaml`, workflows, prompts, schemas, samples, tools (HTTP operations called by `tool` steps) and domain endpoints. |
| Dashboard | `dashboard/` | Generic React console. Calls only the gateway. See [Dashboard](dashboard.md). |
| AppHost | `Flowgent.AppHost` | .NET Aspire host: starts everything with one command and shows logs, traces and metrics. See [Orchestration](orchestration.md). |

Service-to-service calls go directly between services (by name under Aspire, by `localhost` port otherwise), never through the gateway. Every service uses `Flowgent.ServiceDefaults`: health endpoints (`/health`, `/alive`), OpenTelemetry, service discovery and resilience for outgoing HTTP calls.

## Service contracts

Modules and the engine talk only through the HTTP contracts in `Flowgent.Contracts`.

| Direction | Endpoint | Purpose |
|-----------|----------|---------|
| Engine → module | `GET /_module/package` | All definition files as text plus a version (SHA-256 of the files). Served with `ETag`; the engine polls with `If-None-Match` and gets `304` while nothing changed. |
| Engine → module | `POST /_module/tools/{name}` | Invokes a tool. Body `{ runId, moduleId, workflowId, stepId, input }`, response `{ output }` or `{ error }` (`404` unknown tool, `400` expected tool failure, `500` unexpected error). |
| Module → engine | `POST /api/engine/workflows/{id}/runs` | Generic intake, used by domain endpoints (`IEngineClient`). |
| Clients → module | `GET /api/modules/{id}` | Module summary (id, name, description, version, valid, workflows). |

The engine never loads module code: custom logic runs in the module process and is reached over HTTP. `/_module/*` is meant for the private network; the gateway does not route it.

## Module catalog

The engine reads the module list from `Flowgent:Modules` and polls each module in the background (`ModuleCatalogService`), so its own startup never waits for modules.

| Status | Meaning |
|--------|---------|
| `Loading` | No fetch has completed yet. |
| `Available` | Definitions loaded and valid; new inputs are accepted. |
| `Invalid` | Validation errors or a conflict (manifest id differs from the configured id, workflow id already owned by another module). Errors are listed by `GET /api/engine/modules` and the dashboard. |
| `Unavailable` | The last fetch failed. Definitions loaded earlier are kept for in-flight runs; new inputs get `503`. Polling continues every `RefreshSeconds`. |

When a module publishes a new version, the engine loads and compiles it atomically. Runs already in progress keep the definition they started with.

## Runs

Each submitted input becomes a **run** of one workflow. A run has a current step id and a status:

| `RunStatus` | Meaning |
|-------------|---------|
| `Queued` | Saved by the intake, waiting for the inbox processor. |
| `Running` | Steps are executing. |
| `AwaitingInput` | Paused on a `human-input` step. |
| `Completed` | Ended on a `success` terminal step. |
| `Failed` | Ended on a `failure` terminal step, a step failure, or an abort. |
| `Cancelled` | Stopped by engine shutdown. |

`Completed`, `Failed` and `Cancelled` are terminal. Event kinds (`RunEventKind`): `Queued`, `StepStarted`, `StepCompleted`, `AwaitingInput`, `InputReceived`, `Completed`, `Failed`, `Cancelled`.

### Execution

- `MafWorkflowFactory` builds a fresh MAF **declarative** workflow **per run** from the workflow's YAML (`DeclarativeWorkflowBuilder.Build<JsonObject>`), bound to the run's `RunContext`. Concurrent runs never share state. All declarative types are confined to the `Flowgent.Runtime.Maf` seam.
- `MafRunDriver` runs the workflow with JSON checkpointing and translates the declarative event stream into neutral records: per-step `StepStarted`/`StepCompleted` read from `Global.<stepId>` run-state variables, HITL via `RequestInfoEvent`↔`HumanInputRegistry`, and one terminal transition whose outcome comes from the manifest `terminals[]`.
- A step that fails (agent/LLM error, tool error, PowerFx error) ends the run as `Failed` with a descriptive reason; an expected module-tool failure surfaces its `StepFailedException` message (e.g. `Module '<id>' is unavailable…`).
- Each agent call emits an OpenTelemetry activity (`ActivitySource` `Flowgent.Runtime`) tagged with run, module, workflow and step.

### Persistence

The engine stores runs, transition events and inbox messages with EF Core InMemory (`Flowgent.Persistence`, `EngineDbContext`). Every transition updates the run **and** appends an event in a single `SaveChanges` (dual-write), then publishes the update to the gRPC stream. Data is lost when the engine restarts.

## Repository layout

```
backend/
├── Flowgent.slnx                   # one solution: platform, modules, tests
├── Directory.Build.props, Directory.Packages.props, nuget.config   # shared build settings (modules/ imports them)
├── schemas/                        # JSON Schemas of module.yaml, workflows and samples (editors + engine)
├── protos/run_monitor.proto        # gRPC contract (engine + dashboard codegen)
├── src/
│   ├── Engine/                     # Flowgent.Contracts, .Definitions, .Runtime, .Persistence
│   ├── Services/                   # Flowgent.Engine.Service, Flowgent.Gateway
│   ├── Sdk/                        # Flowgent.Module.Sdk, Flowgent.Testing (in-process harness)
│   └── AppHost/                    # Flowgent.AppHost, Flowgent.ServiceDefaults
└── tests/                          # Engine, Gateway, Integration, Architecture tests + Fixtures/ (test-only module)
modules/
├── _template/                      # minimal module to copy
└── support/                        # support-email module (reference implementation)
dashboard/                          # generic monitoring console
docs/                               # platform documentation
```

### Project dependencies

```mermaid
graph TD
    Contracts[Flowgent.Contracts]
    Definitions[Flowgent.Definitions] --> Contracts
    Runtime[Flowgent.Runtime] --> Definitions
    Persistence[Flowgent.Persistence] --> Runtime
    EngineSvc[Flowgent.Engine.Service] --> Runtime & Persistence & Defaults
    Gateway[Flowgent.Gateway] --> Defaults[Flowgent.ServiceDefaults]
    Sdk[Flowgent.Module.Sdk] --> Contracts & Definitions
    Modules[Module services] --> Sdk & Defaults
    Testing[Flowgent.Testing] --> EngineSvc & Gateway & Sdk
```

`Flowgent.Architecture.Tests` enforces the rules:

- `Contracts` has no project references; `Definitions` references only `Contracts`.
- Engine projects and the gateway never reference `modules/` or `Fixtures/` and contain no `Flowgent.Modules.*` types.
- Module services reference only `Flowgent.Module.Sdk`, `Flowgent.Contracts`, `Flowgent.Definitions` and `Flowgent.ServiceDefaults`.
- `Definitions` and `Runtime` do not depend on ASP.NET Core.
- Engine sources and the proto contain no domain terms of the support module.

## Security notes

- There is no end-user or service-to-service authentication. The gateway is the only intended public entry point; engine and module ports, including `/_module/*`, must stay on a private network.
- Secrets (`Flowgent:Llm:ApiKey`, `Flowgent:Secrets:*`) are read from configuration only and never returned by any endpoint. Keep them in user secrets, environment variables or the git-ignored `appsettings.Development.json`.
- Workflow expressions are PowerFx evaluated by the MAF declarative builder (see [Expressions](expressions.md)); module-tool calls validate input/output against the declared schemas.
