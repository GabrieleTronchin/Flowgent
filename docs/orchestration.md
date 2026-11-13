# Local orchestration and observability

[← README](../README.md) · [Architecture](architecture.md) · [Debugging](debugging.md)

## One command: the AppHost

`Flowgent.AppHost` is a .NET Aspire host that starts the engine, every enabled module service, the gateway and the dashboard, and wires their addresses.

```bash
dotnet run --project backend/src/AppHost/Flowgent.AppHost
```

| Resource | Address |
|----------|---------|
| Aspire dashboard (status, logs, traces, metrics) | http://localhost:15080 (opens automatically) |
| Gateway | http://localhost:5080 |
| Monitoring dashboard | http://localhost:5173 |
| Engine | http://localhost:5081 |
| Support module | http://localhost:5082 |
| Template module (when enabled) | http://localhost:5083 |

Requirements: the .NET 10 SDK and Node.js 20.19+ (the AppHost runs `npm run dev` for the dashboard; run `npm install` in `dashboard/` once). The `http` launch profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`, so no development certificate is needed. Aspire packages come from NuGet; no Aspire CLI or workload is required.

### Wiring

`AppHost.cs` registers modules through `ModuleWiring.Add<TProject>(id, extraGatewayRoutes...)`, which for each module:

- sets `Flowgent__Module__EngineUrl=http://engine` on the module,
- adds `Flowgent__Modules__<n>__Id/Url` to the engine,
- adds `Gateway__Modules__<n>__Id/Address` (and `ExtraRoutes`) to the gateway.

Services find each other by resource name (`http://engine`, `http://support`) through service discovery. The engine does not wait for modules: it starts first and retries them. The dashboard waits for the gateway and receives `GATEWAY_URL`.

### Switches

| Setting | Effect |
|---------|--------|
| `AppHost:Modules:<id>:Enabled` | Enables or disables a module (in `backend/src/AppHost/Flowgent.AppHost/appsettings.json`: `support` on, `template` off). |
| `AppHost:LlmProvider` | Overrides `Flowgent:Llm:Provider` of the engine. |

Both can be passed on the command line:

```bash
dotnet run --project backend/src/AppHost/Flowgent.AppHost -- --AppHost:Modules:template:Enabled=true --AppHost:LlmProvider=Demo
```

## Without the AppHost

Each service also runs on its own with the ports of its `http` launch profile and the addresses in its `appsettings.json`:

```bash
dotnet run --project backend/src/Services/Flowgent.Engine.Service    # :5081
dotnet run --project modules/support/service                          # :5082
dotnet run --project backend/src/Services/Flowgent.Gateway           # :5080
cd dashboard && npm run dev                                           # :5173
```

Start them in any order: the engine retries the modules, the gateway answers `503` while a target is down.

## Observability

`Flowgent.ServiceDefaults` is shared by all services:

- **Health**: `/health` (all checks) and `/alive` (liveness) in every environment. Health probes are excluded from traces.
- **Traces**: ASP.NET Core and HttpClient instrumentation plus the sources `Flowgent.*` (one activity per run step from `Flowgent.Runtime`, LLM calls from `Flowgent.Llm`), `Microsoft.Agents.AI*` and `Experimental.Microsoft.Extensions.AI*`. Trace context flows from the gateway through the engine to module tools.
- **Metrics**: ASP.NET Core, HttpClient, runtime and the same `Flowgent.*` / Agent Framework meters.
- **Logs**: OpenTelemetry logging with scopes and formatted messages.
- **Export**: OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (the AppHost sets it, so everything shows up in the Aspire dashboard).
- **Resilience and discovery**: every `HttpClient` gets the standard resilience handler and service discovery. Exceptions: the engine's module clients replace it with their per-module timeout and retries, and the SDK's engine client does not retry intake requests (a retried POST could create a duplicate run).
