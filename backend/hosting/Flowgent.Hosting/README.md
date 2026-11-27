# Flowgent.Hosting

Wires the Flowgent platform into a .NET Aspire AppHost. Both `backend/host` and `samples/host` use it, and it is
packable so an AppHost in another repository can use it too.

## What is inside

| File | Role |
|------|------|
| `FlowgentPlatform.cs` | **Core.** `Add<TEngine, TGateway>` starts engine, gateway and dashboard; `AddModule<T>` wires a module service to the engine and the gateway; `AddMcpServer<T>` starts an MCP server and maps its `serverLabel` on the engine. |

## Rules

- References no Flowgent project: the engine, gateway and module projects come in as the AppHost's `Projects.*` types.
- Depends only on `Aspire.Hosting` and `Aspire.Hosting.JavaScript`.
