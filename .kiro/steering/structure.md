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
backend/
├── Flowgent.slnx                    # single solution (platform + modules); solution folders mirror the physical ones, no "backend" folder
├── Directory.Build.props, Directory.Packages.props, .editorconfig   # one set, applies to every backend project
├── host/                            # Flowgent.AppHost: Aspire AppHost, ModuleWiring, AppHost:Modules toggles
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
│   │                                #   Schemas/ = module/samples JSON Schemas (embedded)
│   ├── Flowgent.Module.Sdk/         # AddFlowgentModule/MapFlowgentModule, ModuleTool<TIn,TOut>, IEngineClient, ModuleDefinitionAssert
│   └── Flowgent.ServiceDefaults/    # health, OpenTelemetry, service discovery, resilience
├── tests/
│   ├── Flowgent.Testing/            # PlatformHarness: engine, modules and gateway in-process on TestServer
│   ├── Flowgent.Integration.Tests/  # support + fixture modules: isolation, module down, version reload, gRPC-Web via gateway
│   ├── Flowgent.Architecture.Tests/ # dependency rules (project files + NetArchTest)
│   └── Fixtures/Flowgent.Fixtures.Module/  # test-only module used by the integration tests (agent, tool, routing, approval)
└── modules/
    ├── _template/                   # definition/, service/ (Flowgent.Modules.Template), tests/, README.md
    ├── jokes/                       # definition/, service/ (tiny example, single llm step)
    └── support/                     # definition/, service/ (Flowgent.Modules.Support), tests/, dummy-apps/, docs/, README.md
        └── mcp/Flowgent.Mcp.Support/  # standalone support MCP server (locateComponent); ServiceDefaults + MCP package only
dashboard/                           # React console: api/, gen/, hooks/, components/, pages/, utils/, types/, __tests__/
deploy/                              # Dockerfiles + GHCR publish workflow
docs/                                # platform documentation (engine, not domain)
```

## Module folder layout

Every module follows the same layout; nothing module-specific lives outside it:

```
backend/modules/<id>/
├── definition/   # module.yaml, workflows/, prompts/, schemas/, samples/
├── service/      # Flowgent.Modules.<Name>.csproj: <Name>Host.Create, Program.cs, Tools/, endpoints, <id>.http
├── mcp/          # optional: standalone MCP server for the module's tools (e.g. support's Flowgent.Mcp.Support)
├── tests/        # Flowgent.Modules.<Name>.Tests.csproj
├── docs/, README.md
└── assets        # e.g. support's dummy-apps/
```

New modules start from `backend/modules/_template` (see its README and `docs/module-authoring.md`). Support domain details: `backend/modules/support/README.md`.

## Dependency rules (Flowgent.Architecture.Tests)

- `Contracts` has no project references; `Definitions` references only `Contracts`.
- `Engine.Core` → `Definitions`; `Persistence` → `Engine.Core`; `Engine.Service` → `Engine.Core`, `Persistence`, `ServiceDefaults`; `Gateway` → `ServiceDefaults`; `Module.Sdk` → `Contracts`, `Definitions`; `Testing` → `Engine.Service`, `Gateway`, `Module.Sdk`.
- Engine projects and the gateway never reference `backend/modules/` or `Fixtures/` and contain no `Flowgent.Modules.*` types.
- Module services reference only `Module.Sdk`, `Contracts`, `Definitions`, `ServiceDefaults`; `Flowgent.Mcp.Support` references only `ServiceDefaults` and the MCP package.
- `Definitions` and `Engine.Core` do not depend on ASP.NET Core; only `Flowgent.Engine.Core.Maf` references the declarative workflows package.
- Engine sources and the proto contain no support-domain terms.

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
- Generated artifacts (e.g. `backend/modules/support/service/support.http`) are checked by tests and regenerated with `UPDATE_SNAPSHOTS=1`.
- Dashboard: Vitest (jsdom) + fast-check in `dashboard/src/__tests__/`.
