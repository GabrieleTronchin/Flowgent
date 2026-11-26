# Flowgent.Engine.Core

The heart of the engine. It takes a request to start a workflow, runs the workflow, and keeps track of what happened. It contains no business (domain) code and no web code: the HTTP and gRPC layer is in `Flowgent.Engine.Service`.

## The run path

```
POST /api/engine/workflows/{id}/runs
        |
        v
IntakeService          checks the input, saves an inbox message and a Queued run
        |
        v
InboxProcessor         picks messages in order and starts each run (does not wait for it)
        |
        v
RunOrchestrator        runs one run from start to finish and records every step
        |
        +-- ModuleCatalog          finds the workflow definition of the module
        +-- MafWorkflowFactory     builds the Microsoft Agent Framework (MAF) workflow from the YAML
        +-- MafRunDriver           runs it and reports plain RunEvent records
              |-- MafAgentProvider     answers the llm steps (real model or Demo mode)
              `-- ModuleToolHandler    runs the tool steps (built-in, MCP server, or module service)
```

`RunOrchestrator` only knows the `IRunDriver` interface. `MafRunDriver` is the only implementation today.

## Core and support

| Folder | Role |
|--------|------|
| `Runs/` | **Core.** Intake, inbox, orchestration, human input, and the run store interfaces. |
| `Modules/` | **Core.** The catalog of loaded modules, the HTTP client to module services, definition sources and the tool invoker. |
| `Maf/` | **Support.** Everything that touches Microsoft Agent Framework: `Building/` (workflow factory, agent provider, tool handler), `Execution/` (the run driver), `Mcp/` (MCP client pool). |
| `Llm/` | **Support.** Chat client setup, Demo mode answers and LLM telemetry. |
| `Configuration/` | Options read from the `Flowgent` configuration section. |
| `RuntimeServiceExtensions.cs` | Registers all of the above in dependency injection. |

## Rules

- Depends only on `Flowgent.Definitions` (and through it `Flowgent.Contracts`).
- Must not depend on ASP.NET Core, on `Flowgent.Persistence` or on any module project.
- MAF types stay inside `Maf/`. Code outside `Maf/` uses `IRunDriver`, `RunEvent` and the other plain types.
- No domain terms of any module.