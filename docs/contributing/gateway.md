# Gateway

[← README](../../README.md) · [Architecture](architecture.md) · [Engine API](../reference/engine-api.md)

`Flowgent.Gateway` is a YARP reverse proxy and the only component the dashboard and external clients call. It listens on `http://localhost:5080` (launch profile `http`, also used under the AppHost).

## Routes

Routes are generated in code from the `Gateway` configuration section, so adding a module is one configuration entry.

| Route | Target |
|-------|--------|
| `/api/engine/{**catch-all}` | Engine service |
| `/flowgent.RunMonitor/{**catch-all}` | Engine service, gRPC-Web stream (dedicated cluster, streamed without buffering, idle timeout `StreamActivityTimeout`, default 12 h) |
| `/api/modules/{id}/{**catch-all}` | The module service registered as `{id}` |
| each `ExtraRoutes` entry of a module | That module service |

`/_module/*` and the services' `/health` endpoints are not routed. The gateway itself maps `/health` and `/alive`.

## Configuration

```json
"Gateway": {
  "Engine": { "Address": "http://localhost:5081" },
  "Modules": [
    { "Id": "support", "Address": "http://localhost:5082", "ExtraRoutes": [ "/api/support/{**catch-all}" ] }
  ]
}
```

| Key | Description |
|-----|-------------|
| `Engine:Address` | Absolute `http(s)` URL of the engine (default `http://localhost:5081`). |
| `Modules[]:Id` | Module id (`^[a-z][a-z0-9_-]*$`, unique). |
| `Modules[]:Address` | Absolute `http(s)` URL of the module service. |
| `Modules[]:ExtraRoutes` | Additional YARP path patterns forwarded to the module (for public URLs outside `/api/modules/{id}`). |
| `StreamActivityTimeout` | Idle timeout of the gRPC-Web stream (default `12:00:00`). |

Invalid ids, duplicate modules or invalid addresses stop the gateway at startup with a descriptive error. Under the AppHost the addresses are service-discovery names (`http://engine`, `http://support`) set by `ModuleWiring`, so `appsettings.json` only matters when services are started one by one.

## Behaviour

- **Correlation id**: the gateway reuses the caller's `X-Correlation-Id` (up to 128 characters) or creates one, forwards it downstream and returns it on the response.
- **Tracing**: W3C trace context (`traceparent`) is propagated by YARP and OpenTelemetry, so a request shows as one trace across gateway, engine and modules in the Aspire dashboard.
- **Service unavailable**: when the target service cannot be reached and nothing has been sent yet, the gateway answers `503` with a problem-details body naming the service:

```json
{ "title": "Service unavailable", "status": 503, "detail": "The 'engine' service is unavailable.", "service": "engine" }
```

- **CORS**: not configured. In development the dashboard calls the gateway through the Vite proxy; other clients call it directly.
- **Authentication**: none. See the security notes in [Architecture](architecture.md#security-notes).
