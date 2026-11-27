# Deployment

[← README](../../README.md) · [Documentation](../README.md) · [Configuration](../reference/configuration.md)

Flowgent runs as three images. They are the same for every use case: your modules are files and, if needed,
MCP servers of your own.

| Image | Role | Port |
|-------|------|------|
| `ghcr.io/gabrieletronchin/flowgent-engine` | Runs the workflows | 8080 |
| `ghcr.io/gabrieletronchin/flowgent-gateway` | Single entry point for the dashboard and clients | 8080 |
| `ghcr.io/gabrieletronchin/flowgent-dashboard` | The console (nginx) | 80 |

Tags: `0.1.0-alpha.1` style versions from release tags, a moving `alpha` channel and `edge` from `master`. The
first release is not tagged yet; until then build the images (see [`deploy/README.md`](../../deploy/README.md)).

## Wiring

```
dashboard --GATEWAY_URL--> gateway --Gateway__Engine__Address--> engine --Flowgent__McpServers__*--> your MCP servers
```

Only the dashboard and the gateway need to be reachable from outside. There is no authentication: keep the
engine and the MCP servers on a private network.

## Your modules

1. Mount one folder per module into the engine under `/definitions/<id>/`.
2. Register each id: `Flowgent__Modules__<n>__Id=<id>`.
3. For each workflow `serverLabel` that calls tools, map the MCP server:
   `Flowgent__McpServers__<n>__Label=<label>` and `Flowgent__McpServers__<n>__Url=http://<server>:<port>`.

```bash
docker run --rm -p 8080:8080 \
  -e Flowgent__Modules__0__Id=notes \
  -v ./notes/definition:/definitions/notes:ro \
  ghcr.io/gabrieletronchin/flowgent-engine:alpha
```

The engine checks the folders every few seconds and loads changes without a restart. A module with errors is
not loaded; `GET /api/engine/modules` (and the dashboard) list its errors with file and message.

## Docker Compose

[`compose/`](../../compose/README.md) has a ready stack: the three images, an empty `definitions/` folder for your
modules and overlays that add the examples of this repository.

## Settings

Model provider, timeouts, polling and every other setting: [Configuration](../reference/configuration.md).
Engine state (runs, events, inbox) is in memory and is lost on restart.
