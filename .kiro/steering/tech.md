# Tech Stack & Build

## Runtime & Language

- .NET 10.0 (`net10.0`), C# `LangVersion: latest`, nullable and implicit usings enabled
- Central package management (`backend/Directory.Packages.props`, transitive pinning; one `backend/Directory.Build.props`); single package source nuget.org (root `nuget.config`)
- Tests run on Microsoft.Testing.Platform (root `global.json`)

## Key Libraries — Backend

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Agents.AI / .OpenAI / .Workflows | 1.27.0 | Workflow execution (WorkflowBuilder, executors), `IChatClient` integration |
| Aspire.AppHost.Sdk, Aspire.Hosting.JavaScript | 13.6.4 | Local orchestration (AppHost, Vite dashboard) |
| Microsoft.Extensions.ServiceDiscovery (+ .Yarp) | 10.11.0 | Service discovery, gateway destination resolution |
| Microsoft.Extensions.Http.Resilience | 10.11.0 | Resilience handlers and per-module retries |
| Yarp.ReverseProxy | 2.3.0 | Gateway |
| YamlDotNet | 18.1.0 | YAML definitions |
| Scriban | 7.5.3 | Templates and expressions (sandboxed) |
| JsonSchema.Net | 9.6.0 | Definition, input, LLM output and tool schema validation |
| OpenTelemetry (Hosting, OTLP, ASP.NET Core, Http, Runtime) | 1.20.x | Logs, traces, metrics |
| Microsoft.EntityFrameworkCore(.InMemory) | 10.0.14 | Engine persistence (runs, events, inbox) |
| Grpc.AspNetCore / .Web | 2.85.0 | `RunMonitor` server stream, gRPC-Web |
| xunit.v3 | 4.0.2 | Tests |
| FsCheck.Xunit.v3 | 3.6.0 | Property-based tests |
| NSubstitute | 6.2.0 | Mocks |
| NetArchTest.Rules | 1.3.2 | Architecture tests |
| Microsoft.AspNetCore.TestHost | 10.0.14 | In-process services (`Flowgent.Testing`) |
| coverlet.MTP | 10.1.0 | Coverage |

## Key Libraries — Frontend (dashboard)

Node.js >= 20.19. React 19, TypeScript 6, Vite 8, Tailwind CSS 4, React Router 7, @xyflow/react (graph), @dagrejs/dagre (layout), @connectrpc/connect-web + @bufbuild/protobuf (gRPC-Web, codegen with buf), recharts, Vitest 4, fast-check 4, Testing Library.

## Common Commands

```bash
# Build and test everything (backend + modules)
dotnet build backend/Flowgent.slnx
dotnet test --solution backend/Flowgent.slnx

# Single test projects
dotnet test --project backend/engine/Flowgent.Engine.Tests
dotnet test --project backend/gateway/Flowgent.Gateway.Tests
dotnet test --project backend/tests/Flowgent.Integration.Tests
dotnet test --project backend/tests/Flowgent.Architecture.Tests
dotnet test --project backend/modules/support/tests
dotnet test --project backend/modules/_template/tests

# Whole platform (engine, enabled modules, gateway :5080, dashboard :5173, Aspire dashboard :15080)
dotnet run --project backend/host
dotnet run --project backend/host -- --AppHost:Modules:template:Enabled=true

# Services one by one
dotnet run --project backend/engine/Flowgent.Engine.Service   # :5081
dotnet run --project backend/modules/support/service                         # :5082
dotnet run --project backend/gateway/Flowgent.Gateway          # :5080

# Dashboard
cd dashboard && npm install
cd dashboard && npm run dev
cd dashboard && npm run typecheck
cd dashboard && npm run test -- --run
cd dashboard && npm run build
cd dashboard && npm run generate     # regenerate src/gen from backend/engine/protos/run_monitor.proto
```

## Configuration

- Engine (`Flowgent` section): `EnableVisualization` (gRPC stream + agents endpoint), `SequentialProcessing`, `InboxPollingIntervalSeconds`, `DefaultToolTimeoutSeconds`, `Modules[] { Id, Url, Enabled, RefreshSeconds, TimeoutSeconds, Retries, RetryDelayMilliseconds }`, `Secrets:<moduleId>:<key>` (http step headers).
- LLM (`Flowgent:Llm`): `Provider` (`Demo` default | `OpenAI`), `ModelName` (`gpt-4o-mini`), `ApiKey`, optional `Endpoint` (any OpenAI-compatible server), `DemoLatencyMs` (1500). Keep the key in the git-ignored `appsettings.Development.json` of the engine or in environment variables. `GET /api/engine/config` exposes only `sequentialProcessing`, `demoMode`, `llmProvider`, `llmModel`.
- Module SDK (`Flowgent:Module`): `DefinitionPath` (`../definition`), `EngineUrl` (`http://localhost:5081`), `FailOnInvalidDefinition` (false).
- Gateway (`Gateway`): `Engine:Address`, `Modules[] { Id, Address, ExtraRoutes[] }`, `StreamActivityTimeout`.
- AppHost: `AppHost:Modules:<id>:Enabled`, `AppHost:LlmProvider`.
- Ports (launch profiles, also used by Aspire): gateway 5080, engine 5081, support 5082, template 5083, dashboard 5173.

## Code Style (.editorconfig enforced)

- File-scoped namespaces (enforced as error)
- 4-space indentation, LF line endings, final newline required
- PascalCase for public members, `_camelCase` for private fields
- Prefer `var` when type is apparent
- Prefer pattern matching, switch expressions, and expression-bodied members
- Prefer target-typed `new()` when type is apparent
- Prefer collection expressions over traditional syntax
