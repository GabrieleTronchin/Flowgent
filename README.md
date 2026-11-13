# Flowgent

![CI](https://github.com/GabrieleTronchin/Flowgent/actions/workflows/ci.yml/badge.svg)
![Dashboard CI](https://github.com/GabrieleTronchin/Flowgent/actions/workflows/dashboard-ci.yml/badge.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)
![React](https://img.shields.io/badge/React-19-61dafb?logo=react)
![TypeScript](https://img.shields.io/badge/TypeScript-6-3178c6?logo=typescript&logoColor=white)
![License](https://img.shields.io/github/license/GabrieleTronchin/Flowgent)

**A spec-driven AI experiment.**

Flowgent is a base for building LLM workflows. Workflows are **Microsoft Agent Framework declarative** documents (actions, PowerFx expressions, prompts, JSON schemas, human review) run by a domain-agnostic engine. Each business domain is a **module**: a small service that owns its definitions and custom code. A generic dashboard monitors every module, and one command starts everything.

- **Engine**: validates and runs workflows, owns runs, events, the inbox, human input, LLM access (Demo or OpenAI-compatible) and telemetry.
- **Modules**: publish their definitions and expose tools (C# operations called by workflow steps) and domain endpoints.
- **Gateway**: the single entry point for the dashboard and clients.
- **Dashboard**: adapts to the selected workflow (graph, form, approvals, runs, agents, events).
- **Demo mode** by default: no API key needed, every workflow runs offline with the responses declared in YAML.

## Topology

```mermaid
graph LR
    Browser[Dashboard :5173] --> GW[Gateway :5080]
    Client[HTTP clients] --> GW
    GW -->|/api/engine/**, gRPC-Web| ENG[Engine :5081]
    GW -->|"/api/modules/{id}/**"| MOD[Module services]
    ENG -->|definitions, tools| MOD
    MOD -->|intake| ENG
    ENG --> LLM[(Demo / OpenAI-compatible)]
```

Details: [Architecture](docs/architecture.md).

## Modules

| Module | Folder | Description |
|--------|--------|-------------|
| Support | [`modules/support`](modules/support/README.md) | Reference module: triages support emails and proposes code fixes after human approval. |
| Jokes | [`modules/jokes`](modules/jokes/definition/module.yaml) | Tiny example: one `llm` step turns a topic into a one-liner joke. A good starting point to read. |
| Template | [`modules/_template`](modules/_template/README.md) | Minimal module to copy when creating a new one (disabled by default). |

A test-only fixture module (`backend/tests/Fixtures/Flowgent.Fixtures.Module`) covers every step kind in the integration tests.

## How modules work

1. A module folder contains `definition/` (`module.yaml`, `workflows/`, `prompts/`, `schemas/`, `samples/`), `service/` (ASP.NET Core on `Flowgent.Module.Sdk`), `tests/` and its documentation.
2. The module service publishes its definition files at `GET /_module/package`. The engine polls every configured module, validates the definitions and compiles each workflow into an Agent Framework graph.
3. A client submits an input to `POST /api/engine/workflows/{id}/runs` (or to a module endpoint that forwards it). The engine validates it against the input schema, stores it in the inbox and returns `202`.
4. The run executes step by step: `llm`, `rule`, `map`, `human-input`, `tool` (calls the module service), `http` (calls an external API) and `terminal`.
5. Every transition is persisted and streamed to the dashboard.

Workflows are **Microsoft Agent Framework declarative** documents — a `trigger` with an ordered list of `actions`, with [PowerFx](docs/expressions.md) expressions (`=Expr`). An excerpt from [`template-hello.yaml`](modules/_template/definition/workflows/template-hello.yaml) (an LLM summary, then a module tool over MCP, then a terminal):

```yaml
kind: Workflow
trigger:
  kind: OnConversationStart
  id: template-hello
  actions:
    - kind: InvokeAzureAgent          # an LLM step, resolved by name through our IChatClient (no Foundry)
      id: summarize
      agent: { name: summarize|template_writer }
      input: { messages: =UserMessage(Concatenate("Text: ", First(Local.intake).text)) }
      output: { messages: Local.summarize }
    - kind: InvokeMcpTool             # a domain tool, invoked over MCP
      id: count_words
      serverLabel: template
      toolName: template.wordCount
      arguments: { text: =Local.summary }
      output: { result: Local.wc }
    - kind: SendActivity              # terminal output
      id: done
      activity: { text: ["{Global.__message}"] }
```

The engine loads these with the declarative workflow builder; there is no custom YAML format, validator or Scriban template engine. To add a module, copy `modules/_template` and follow its README: no change to the engine, the gateway or the dashboard is needed. See [Module authoring](docs/module-authoring.md) and the [Workflow reference](docs/workflow-reference.md).

## Getting started

Requirements: .NET 10 SDK, Node.js 20.19+.

```bash
git clone <repository-url> flowgent
cd flowgent
cd dashboard && npm install && cd ..

# Engine, enabled modules, gateway and dashboard (.NET Aspire)
dotnet run --project backend/src/AppHost/Flowgent.AppHost
```

| What | Where |
|------|-------|
| Monitoring dashboard | http://localhost:5173 |
| Gateway (API entry point) | http://localhost:5080 |
| Aspire dashboard (logs, traces, metrics) | http://localhost:15080 |

Enable the template module with `-- --AppHost:Modules:template:Enabled=true`. Services can also be started one by one; see [Orchestration](docs/orchestration.md).

The engine starts in Demo mode. To use a real model, set `Flowgent:Llm` in the git-ignored `backend/src/Services/Flowgent.Engine.Service/appsettings.Development.json` or in environment variables; see [Demo mode](docs/demo-mode.md).

### Build and test

```bash
dotnet build backend/Flowgent.slnx
dotnet test --solution backend/Flowgent.slnx          # engine, gateway, architecture, integration and module tests

cd dashboard
npm run typecheck
npm run test -- --run
npm run build
```

Tests run in-process (`Flowgent.Testing.PlatformHarness`): no ports, no Aspire, Demo mode.

## Repository structure

```
global.json                         # .NET test runner (Microsoft.Testing.Platform), read from the working directory
backend/
├── Flowgent.slnx                   # single solution (backend and modules)
├── Directory.Build.props, Directory.Packages.props, nuget.config   # shared build settings and package versions
├── schemas/                        # JSON Schemas of the YAML formats
├── protos/run_monitor.proto        # gRPC run stream
├── src/
│   ├── Engine/                     # Flowgent.Contracts, .Definitions, .Runtime, .Persistence
│   ├── Services/                   # Flowgent.Engine.Service, Flowgent.Gateway
│   ├── Sdk/                        # Flowgent.Module.Sdk, Flowgent.Testing
│   └── AppHost/                    # Flowgent.AppHost, Flowgent.ServiceDefaults
└── tests/                          # Engine, Gateway, Architecture, Integration tests, Fixtures/
modules/
├── _template/                      # module template
├── jokes/                          # tiny example module (single llm step, no tools)
└── support/                        # support module (definition, service, tests, dummy-apps, docs)
dashboard/                          # React monitoring console
docs/                               # platform documentation
```

## Tech stack

| Area | Technology |
|------|-----------|
| Runtime | .NET 10, C# latest |
| Execution | Microsoft Agent Framework Workflows 1.27 |
| LLM | Microsoft.Extensions.AI `IChatClient` (Demo, OpenAI-compatible) with structured output |
| Definitions | MAF declarative workflows (`Microsoft.Agents.AI.Workflows.Declarative`) + PowerFx; module envelope in YAML (YamlDotNet), JSON Schema (JsonSchema.Net) |
| Services | ASP.NET Core minimal APIs, YARP gateway, gRPC-Web |
| Orchestration | .NET Aspire 13, OpenTelemetry |
| Persistence | EF Core 10 InMemory |
| Frontend | React 19, TypeScript 6, Vite 8, Tailwind CSS 4, React Flow, dagre |
| Testing | xUnit v3 (Microsoft.Testing.Platform), FsCheck, NSubstitute, NetArchTest, Vitest, fast-check |

## Documentation

| Document | Content |
|----------|---------|
| [Architecture](docs/architecture.md) | Services, contracts, module catalog, run model, persistence, dependency rules |
| [Workflow reference](docs/workflow-reference.md) | `module.yaml`, MAF declarative workflows, action kinds, samples, validation |
| [Expressions](docs/expressions.md) | PowerFx expressions and conditions in declarative workflows, available data |
| [Module authoring](docs/module-authoring.md) | Creating a module from the template: SDK, tools, endpoints, registration, tests |
| [Gateway](docs/gateway.md) | Routes, configuration, correlation id, 503 handling |
| [Engine API](docs/engine-api.md) | `/api/engine` endpoints, gRPC stream, engine configuration |
| [Demo mode](docs/demo-mode.md) | LLM providers and demo responses |
| [Inbox](docs/inbox.md) | Asynchronous intake and processing |
| [Human input](docs/human-input.md) | Approve, reject and abort |
| [Dashboard](docs/dashboard.md) | Pages, graph, form, real-time updates |
| [Orchestration](docs/orchestration.md) | AppHost, standalone services, observability |
| [Debugging](docs/debugging.md) | `.http` files, troubleshooting, traces |

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
