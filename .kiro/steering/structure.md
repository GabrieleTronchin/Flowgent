# Project Structure

## Topology

```
Dashboard (:5173) ─┐
HTTP clients ──────┴─> Gateway (:5080) ─┬─> Engine service (:5081)
                                        └─> Module services (support :5082, template :5083)
Engine ──(GET /_module/package)──> Module services
Engine ──(MCP over Streamable HTTP, per Flowgent:McpServers[])──> Domain MCP servers (e.g. Flowgent.Mcp.Support)
Module services ──(POST /api/engine/workflows/{id}/runs)──> Engine
```

Service-to-service calls go directly (service discovery under Aspire), never through the gateway.

## Repository layout

```
.editorconfig, .gitattributes, global.json   # repo-wide (global.json: test runner, read from the working directory)
backend/
├── Flowgent.slnx                    # single solution (backend + modules, module paths are ../modules/...)
├── Directory.Build.props, Directory.Packages.props, nuget.config
├── schemas/                         # module/workflow/samples JSON Schemas (embedded in Flowgent.Definitions)
├── protos/run_monitor.proto         # gRPC contract (engine + dashboard codegen)
├── src/
│   ├── Engine/
│   │   ├── Flowgent.Contracts/      # HTTP DTOs: module package, tool invocation, engine API views, RunStatus/RunEventKind
│   │   ├── Flowgent.Definitions/    # YAML → JsonNode reader, model records, ModuleLoader, DefinitionValidator
│   │   ├── Flowgent.Runtime/        # WorkflowCompiler, RunOrchestrator, RunRegistry, HumanInputRegistry, IntakeService,
│   │   │                            #   InboxProcessor, ModuleCatalog(+Service), ModuleToolInvoker, LLM (Demo, telemetry);
│   │   │                            #   Maf/ = ModuleToolHandler (MCP-over-HTTP branch), McpClientPool, IMcpTransportFactory
│   │   └── Flowgent.Persistence/    # EF Core InMemory EngineDbContext, EfRunStore (dual-write), EfInboxStore
│   ├── Services/
│   │   ├── Flowgent.Engine.Service/ # EngineHost, /api/engine endpoints, RunMonitorService (gRPC), engine.http
│   │   ├── Flowgent.Gateway/        # GatewayHost, GatewayRoutes, GatewayMiddleware (correlation id, 503)
│   │   └── Flowgent.Mcp.Support/    # standalone support MCP server (locateComponent); ServiceDefaults + MCP package only
│   ├── Sdk/
│   │   ├── Flowgent.Module.Sdk/     # AddFlowgentModule/MapFlowgentModule, ModuleTool<TIn,TOut>, IEngineClient, ModuleDefinitionAssert
│   │   └── Flowgent.Testing/        # PlatformHarness: engine, modules and gateway in-process on TestServer
│   └── AppHost/
│       ├── Flowgent.AppHost/        # Aspire AppHost, ModuleWiring, AppHost:Modules toggles
│       └── Flowgent.ServiceDefaults/ # health, OpenTelemetry, service discovery, resilience
└── tests/
    ├── Flowgent.Engine.Tests/       # definitions, runtime, persistence, LLM, endpoints (+ FsCheck properties)
    ├── Flowgent.Gateway.Tests/      # routes, forwarding, correlation id, 503
    ├── Flowgent.Integration.Tests/  # support + fixture modules: isolation, module down, version reload, gRPC-Web via gateway
    ├── Flowgent.Architecture.Tests/ # dependency rules (project files + NetArchTest)
    └── Fixtures/Flowgent.Fixtures.Module/  # test-only module covering every step kind
modules/
├── Directory.Build.props, Directory.Packages.props   # import the backend/ ones; nuget.config mirrors backend/nuget.config
├── _template/                       # definition/, service/ (Flowgent.Modules.Template), tests/, README.md
└── support/                         # definition/, service/ (Flowgent.Modules.Support), tests/, dummy-apps/, docs/, README.md
dashboard/                           # React console: api/, gen/, hooks/, components/, pages/, utils/, types/, __tests__/
docs/                                # platform documentation (engine, not domain)
```

## Module folder layout

Every module follows the same layout; nothing module-specific lives outside it:

```
modules/<id>/
├── definition/   # module.yaml, workflows/, prompts/, schemas/, samples/
├── service/      # Flowgent.Modules.<Name>.csproj: <Name>Host.Create, Program.cs, Tools/, endpoints, <id>.http
├── tests/        # Flowgent.Modules.<Name>.Tests.csproj
├── docs/, README.md
└── assets        # e.g. support's dummy-apps/
```

New modules start from `modules/_template` (see its README and `docs/module-authoring.md`). Support domain details: `modules/support/README.md`.

## Dependency rules (Flowgent.Architecture.Tests)

- `Contracts` has no project references; `Definitions` references only `Contracts`.
- `Runtime` → `Definitions`; `Persistence` → `Runtime`; `Engine.Service` → `Runtime`, `Persistence`, `ServiceDefaults`; `Gateway` → `ServiceDefaults`; `Module.Sdk` → `Contracts`, `Definitions`; `Testing` → `Engine.Service`, `Gateway`, `Module.Sdk`.
- Engine projects and the gateway never reference `modules/` or `Fixtures/` and contain no `Flowgent.Modules.*` types.
- Module services reference only `Module.Sdk`, `Contracts`, `Definitions`, `ServiceDefaults`.
- `Definitions` and `Runtime` do not depend on ASP.NET Core.
- Engine sources and the proto contain no support-domain terms.

## Key patterns

- **Hosts as factories**: every service exposes `XxxHost.Create(WebApplicationOptions, Action<WebApplicationBuilder>?)`; `Program.cs` calls it and tests use it in-process.
- **Pull model**: the engine polls module packages with ETag; new versions load atomically, in-flight runs keep their definition.
- **Per-run graphs**: `WorkflowCompiler` builds one Agent Framework workflow per run with one executor per step; `StepMessage(NextStepId)` is the only message.
- **Result types** for expected failures; `StepFailedException(stepId, reason)` ends a run as `Failed`.
- **Configuration-driven**: modules (engine `Flowgent:Modules`, gateway `Gateway:Modules`, AppHost `AppHost:Modules`), never hard-coded.
- **Async-first** with `CancellationToken` for all I/O.

## Testing conventions

- xUnit v3 on Microsoft.Testing.Platform; FsCheck for properties; NSubstitute where mocks are needed.
- Platform and module tests run services in-process with `PlatformHarness` in Demo mode (no ports, no Aspire).
- Module tests validate the definition with `ModuleDefinitionAssert.IsValid` and run workflows end-to-end.
- Generated artifacts (e.g. `modules/support/service/support.http`) are checked by tests and regenerated with `UPDATE_SNAPSHOTS=1`.
- Dashboard: Vitest (jsdom) + fast-check in `dashboard/src/__tests__/`.
