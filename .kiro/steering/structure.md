# Project Structure

## Topology

```
Dashboard (:5173) ─┐
HTTP clients ──────┴─> Gateway (:5080) ─┬─> Engine service (:5081)
                                        └─> Module services (support :5082, template :5083, jokes :5084)
Engine ──(GET /_module/package)──> Module services
Engine ──(MCP over Streamable HTTP, per Flowgent:McpServers[])──> Domain MCP servers (e.g. Flowgent.Mcp.Support)
Module services ──(POST /api/engine/workflows/{id}/runs)──> Engine
```

Service-to-service calls go directly (service discovery under Aspire), never through the gateway.

## Repository layout

```
.gitattributes, .gitignore, global.json, nuget.config   # repo-wide (global.json: test runner, read from the working directory; nuget.config: nuget.org-only feed)
Directory.Build.props, Directory.Packages.props, .editorconfig   # one set at the root, applies to every .NET project
backend/                             # the core platform: builds and tests without samples/
├── Flowgent.Core.slnx               # core solution; solution folders mirror the physical ones, no "backend" folder
├── host/                            # Flowgent.AppHost: core AppHost (engine, gateway, dashboard, template)
├── hosting/Flowgent.Hosting/        # FlowgentPlatform: AppHost wiring shared by both AppHosts (packable)
├── engine/
│   ├── Flowgent.Engine.Service/     # EngineHost, endpoints/ (one IEndpoint class per resource), RunMonitorService (gRPC), engine.http
│   ├── Flowgent.Engine.Core/        # Runs/ (core: Intake, Execution, Store, HumanInput), Modules/ (Catalog, Client, Sources, Tools),
│   │                                #   Maf/ (everything that touches Microsoft Agent Framework: Building, Execution, Mcp), Llm/ (chat client, Demo, telemetry),
│   │                                #   Configuration/ (options), RuntimeServiceExtensions (DI wiring), README.md (run path)
│   ├── Flowgent.Persistence/        # EF Core InMemory EngineDbContext, EfRunStore (dual-write), EfInboxStore
│   ├── Flowgent.Engine.Tests/       # definitions, runtime, persistence, LLM, endpoints (+ FsCheck properties)
│   └── protos/run_monitor.proto     # gRPC contract (engine + dashboard codegen)
├── gateway/
│   ├── Flowgent.Gateway/            # GatewayHost, GatewayRoutes, GatewayMiddleware (correlation id, 503)
│   └── Flowgent.Gateway.Tests/      # routes, forwarding, correlation id, 503
├── shared/
│   ├── Flowgent.Contracts/          # HTTP DTOs: module package, tool invocation, engine API views, RunStatus/RunEventKind
│   ├── Flowgent.Definitions/        # YAML → JsonNode reader, model records, ModuleLoader, EnvelopeValidator;
│   │                                #   Schemas/ = module/samples/demo JSON Schemas (embedded); DemoTemplate (demo file rules)
│   ├── Flowgent.Module.Sdk/         # AddFlowgentModule/MapFlowgentModule, ModuleTool<TIn,TOut>, IEngineClient, ModuleDefinitionAssert
│   └── Flowgent.ServiceDefaults/    # health, OpenTelemetry, service discovery, resilience
├── template/                        # module template: definition/ (with a demo file), service/ (Flowgent.Modules.Template), tests/, README.md
└── tests/
    ├── Flowgent.Testing/            # PlatformHarness: engine, modules and gateway in-process on TestServer
    ├── Flowgent.Integration.Tests/  # fixture + template modules: module down, version reload, gRPC-Web via gateway
    ├── Flowgent.Architecture.Tests/ # core dependency rules (project files + NetArchTest), no reference to samples/
    └── Fixtures/Flowgent.Fixtures.Module/  # test-only module used by the integration tests (agent, tool, routing, approval)
samples/                             # example modules (not the product); may move to their own repository
├── Flowgent.Samples.slnx            # core projects + samples
├── host/                            # Flowgent.Samples.AppHost: platform + support (+ MCP) + jokes
├── jokes/                           # definition/, service/ (tiny example, single agent step)
├── support/                         # definition/, service/ (Flowgent.Modules.Support), tests/, dummy-apps/, docs/, README.md
│   └── mcp/Flowgent.Mcp.Support/    # standalone support MCP server (locateComponent); ServiceDefaults + MCP package only
└── tests/Flowgent.Samples.Tests/    # samples dependency rules + multi-module isolation (support + fixture)
dashboard/                           # React console: api/, gen/, hooks/, components/, pages/, utils/, types/, __tests__/
deploy/                              # Dockerfiles + GHCR publish workflow
compose/                             # generic Docker Compose stack + samples/ overlays + definitions/ for user modules
docs/                                # README.md (index), getting-started.md, examples.md, guides/, reference/ (users), contributing/ (contributors)
CONTRIBUTING.md                      # contributor entry point (solutions, run, test, rules)
```

## Module folder layout

Every module follows the same layout (`backend/template`, `samples/<id>`); nothing module-specific lives outside it:

```
<module folder>/
├── definition/   # module.yaml, workflows/, prompts/, schemas/, samples/
├── service/      # Flowgent.Modules.<Name>.csproj: <Name>Host.Create, Program.cs, Tools/, endpoints, <id>.http
├── mcp/          # optional: standalone MCP server for the module's tools (e.g. support's Flowgent.Mcp.Support)
├── tests/        # Flowgent.Modules.<Name>.Tests.csproj
├── docs/, README.md
└── assets        # e.g. support's dummy-apps/
```

New modules start from `docs/guides/writing-modules.md` (definition folder + MCP server) or, for a module service, from `backend/template` (see its README). Support domain details: `samples/support/README.md`.

## Dependency rules (Flowgent.Architecture.Tests)

- `Contracts` has no project references; `Definitions` references only `Contracts`.
- `Engine.Core` → `Definitions`; `Persistence` → `Engine.Core`; `Engine.Service` → `Engine.Core`, `Persistence`, `ServiceDefaults`; `Gateway` → `ServiceDefaults`; `Module.Sdk` → `Contracts`, `Definitions`; `Testing` → `Engine.Service`, `Gateway`, `Module.Sdk`.
- Engine projects and the gateway never reference `template/`, `samples/` or `Fixtures/` and contain no `Flowgent.Modules.*` types; no core project references `samples/`.
- Module services reference only `Module.Sdk`, `Contracts`, `Definitions`, `ServiceDefaults`; `Flowgent.Mcp.Support` references only `ServiceDefaults` and the MCP package (checked in `samples/tests`).
- `Definitions` and `Engine.Core` do not depend on ASP.NET Core; only `Flowgent.Engine.Core.Maf` references the declarative workflows package.
- Engine sources and the proto contain no sample domain terms or workflow ids (demo answers are declared by each module).

## Key patterns

- **Hosts as factories**: every service exposes `XxxHost.Create(WebApplicationOptions, Action<WebApplicationBuilder>?)`; `Program.cs` calls it and tests use it in-process.
- **Pull model**: the engine polls module packages with ETag; new versions load atomically, in-flight runs keep their definition.
- **Per-run graphs**: `MafWorkflowFactory` builds one MAF declarative workflow per run from the workflow YAML (`DeclarativeWorkflowBuilder`) and `MafRunDriver` runs it with JSON checkpointing; every declarative type stays inside `Flowgent.Engine.Core.Maf`.
- **Tool routing**: `ModuleToolHandler` sends reserved `flowgent.*` tools in-process, labels mapped in `Flowgent:McpServers[]` to an MCP server (`McpClientPool`), and any other label to `ModuleToolInvoker` (the `/_module/tools` fallback).
- **Result types** for expected failures; `StepFailedException(stepId, reason)` ends a run as `Failed`.
- **Configuration-driven**: modules (engine `Flowgent:Modules`, gateway `Gateway:Modules`, AppHost `AppHost:Modules`), never hard-coded.
- **Async-first** with `CancellationToken` for all I/O.

## Testing conventions

- xUnit v3 on Microsoft.Testing.Platform; FsCheck for properties; NSubstitute where mocks are needed.
- Platform and module tests run services in-process with `PlatformHarness` in Demo mode (no ports, no Aspire).
- Module tests validate the definition with `ModuleDefinitionAssert.IsValid` and run workflows end-to-end.
- Generated artifacts (e.g. `samples/support/service/support.http`) are checked by tests and regenerated with `UPDATE_SNAPSHOTS=1`.
- Dashboard: Vitest (jsdom) + fast-check in `dashboard/src/__tests__/`.
