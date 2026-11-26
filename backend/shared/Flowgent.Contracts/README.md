# Flowgent.Contracts

The shared data shapes (DTOs) that travel over HTTP between the dashboard, the gateway, the engine and the module services. Type names, namespaces and JSON names are part of the public contract: do not change them casually.

## What is inside

| Folder | Role |
|--------|------|
| `Api/` | **Core.** What the engine API returns: runs, events, workflows, modules, inbox, human inputs, telemetry. Also `EngineRoutes`. |
| `Modules/` | **Core.** What the engine and module services exchange: the module package and tool invocation request and response. Also `ModuleRoutes`. |
| `Runs/` | **Core.** `RunStatus` and `RunEventKind`. |

Each file holds one type (a few very small helper types sit below the main one).

## Rules

- No project references and no logic beyond tiny helpers. Every other project may use it.
- Must not depend on ASP.NET Core or on any other Flowgent project.