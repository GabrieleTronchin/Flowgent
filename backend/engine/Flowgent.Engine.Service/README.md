# Flowgent.Engine.Service

The engine as a running web service (port 5081). It exposes `Flowgent.Engine.Core` over HTTP (`/api/engine`) and over gRPC (`RunMonitor` stream), and wires in persistence. It holds no workflow logic.

## What is inside

| Folder or file | Role |
|----------------|------|
| `Endpoints/` | **Core.** One class per resource (runs, inbox, human input, catalog, monitoring). `Mapping/` turns core types into API views. |
| `RunMonitorService.cs` | **Core.** The gRPC stream the dashboard listens to. |
| `EngineHost.cs`, `Program.cs` | **Support.** Builds and starts the host. Tests call `EngineHost.Create` in-process. |
| `engine.http` | Sample requests. |

## Rules

- May depend on `Flowgent.Engine.Core`, `Flowgent.Persistence` and `Flowgent.ServiceDefaults`.
- Must not reference module projects. Service-to-service calls go directly, not through the gateway.