# Flowgent.Gateway

The single entry point (port 5080) for the dashboard and for HTTP clients. It forwards requests with YARP, based on configuration only.

## What is inside

| File | Role |
|------|------|
| `GatewayRoutes.cs`, `GatewayProxyConfigProvider.cs` | **Core.** Build the routes: `/api/engine/**` and the gRPC-Web stream to the engine, `/api/modules/{id}/**` to module services. |
| `GatewayMiddleware.cs` | **Core.** Correlation id and the `503` problem details when a target is down. |
| `GatewayOptions.cs`, `ModuleRouteOptions.cs`, `ServiceAddressOptions.cs` | **Support.** Configuration (`Gateway` section). |
| `GatewayHost.cs`, `Program.cs` | **Support.** Builds and starts the host. |

## Rules

- Depends only on `Flowgent.ServiceDefaults`.
- Must not reference the engine projects or any module project: modules are added in configuration, not in code.