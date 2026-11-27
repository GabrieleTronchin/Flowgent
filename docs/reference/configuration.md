# Configuration

[← README](../../README.md) · [Documentation](../README.md) · [Deployment](../guides/deployment.md)

Every setting is a .NET configuration key. In containers set it as an environment variable, replacing `:` with
`__` and using indexes for lists: `Flowgent:Modules:0:Id` becomes `Flowgent__Modules__0__Id`.

The images ship no modules and no MCP servers: they are all configured here.

## Engine (`flowgent-engine`)

### General (`Flowgent`)

| Key | Default | Description |
|-----|---------|-------------|
| `Definitions:Path` | `/definitions` in the image | Folder of module definitions, one sub-folder per module id (`<Path>/<id>/module.yaml`). |
| `Modules[]` | none | The modules the engine loads, see below. |
| `McpServers[]` | none | `{ Label, Url }`: maps a workflow `serverLabel` to an MCP server (Streamable HTTP). The label `flowgent` is reserved. |
| `SequentialProcessing` | `false` | Start a run only when no other run is running or awaiting input. |
| `InboxPollingIntervalSeconds` | `5` (`1` in the image) | How often the inbox is checked for new inputs. |
| `DefaultToolTimeoutSeconds` | `30` | Tool timeout when neither the tool nor the module sets one. |
| `EnableVisualization` | `true` | Enables the gRPC run stream and the agents endpoint used by the dashboard. |

### Modules (`Flowgent:Modules[]`)

| Key | Default | Description |
|-----|---------|-------------|
| `Id` | required | Module id; must match `id` in `module.yaml`. |
| `Url` | empty | Module service address. Only needed when the definition is not on the volume (the engine then pulls `GET /_module/package`) or for tools served by the module service. |
| `Enabled` | `true` | `false` ignores the entry. |
| `RefreshSeconds` | `5` | How often the definition is checked for changes (also the retry interval while unavailable). |
| `TimeoutSeconds` | `30` | Timeout of a call to the module service. |
| `Retries` | `2` | Retries of transient failures (connection errors, 5xx, 408, 429). |
| `RetryDelayMilliseconds` | `500` | Delay between retries. |

A module whose folder exists under `Definitions:Path` is read from there; otherwise it is pulled from `Url`.
Changes are loaded without a restart; runs already in progress keep the definition they started with.

### LLM (`Flowgent:Llm`)

| Key | Default | Description |
|-----|---------|-------------|
| `Provider` | `Demo` | `Demo` (no model, answers from each module's demo files) or `OpenAI` (any OpenAI-compatible API). |
| `ModelName` | `gpt-4o-mini` | Model used with `OpenAI`. |
| `ApiKey` | none | Required with `OpenAI`. Never returned by any endpoint. |
| `Endpoint` | OpenAI | Absolute URL of another OpenAI-compatible server (for example `http://localhost:11434/v1` for Ollama). |
| `DemoLatencyMs` | `1500` | Simulated latency of a demo answer, so progress is visible. `0` is instant. |

The image listens on port `8080` (`ASPNETCORE_HTTP_PORTS`).

## Gateway (`flowgent-gateway`)

| Key | Default | Description |
|-----|---------|-------------|
| `Gateway:Engine:Address` | `http://localhost:5081` | Engine address (in containers, for example `http://engine:8080`). |
| `Gateway:Modules[]:Id` | none | Module id; routes `/api/modules/<id>/**` to the module service. Only needed for modules that expose their own endpoints. |
| `Gateway:Modules[]:Address` | none | Module service address. |
| `Gateway:Modules[]:ExtraRoutes[]` | none | More path patterns forwarded to the module. |
| `Gateway:StreamActivityTimeout` | `12:00:00` | Idle timeout of the run stream. |

The image listens on port `8080`.

## Dashboard (`flowgent-dashboard`)

| Variable | Default | Description |
|----------|---------|-------------|
| `GATEWAY_URL` | `http://gateway:8080` | Where nginx forwards `/api/` and the run stream. |
| `NGINX_RESOLVER` | `127.0.0.11` | DNS server used to find the gateway (Docker's embedded DNS). |

The image listens on port `80`.

## Module services (Module SDK, advanced)

Only for modules built as a .NET service with the Module SDK (see [Writing modules](../guides/writing-modules.md#advanced-a-module-service-with-the-module-sdk)).

| Key | Default | Description |
|-----|---------|-------------|
| `Flowgent:Module:DefinitionPath` | `../definition` | Definition folder, absolute or relative to the content root. |
| `Flowgent:Module:EngineUrl` | `http://localhost:5081` | Engine address used to submit inputs. |
| `Flowgent:Module:FailOnInvalidDefinition` | `false` | Stop the service when the definition is invalid (by default it starts and logs the errors). |

## Telemetry (all services)

`OTEL_EXPORTER_OTLP_ENDPOINT` sends logs, traces and metrics over OTLP. Health endpoints: `/health` and `/alive`.
