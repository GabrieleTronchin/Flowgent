# Reference stack (Docker Compose)

> Not tested yet: this stack was prepared without Docker available. Run it once and fix what
> it turns up before relying on it (task 4.3 in the migration spec).

Runs the whole platform from the four images, in Demo mode (no API key):

| Service | Role | Host port |
|---------|------|-----------|
| `dashboard` | React console (nginx), forwards `/api` and the run stream to the gateway | 5173 |
| `gateway` | Single entry point (YARP) | 5080 |
| `engine` | Runs the workflows; reads the support definitions from a mounted folder | none |
| `support-mcp` | Example MCP server with the `locateComponent` tool | none |

```bash
cd compose
docker compose up --build
```

Open http://localhost:5173, pick the support workflow and submit one of its samples.
Stop with `docker compose down`.

## How it fits together

- The engine image contains no definitions. `../backend/modules/support/definition` is mounted
  read-only at `/definitions/support`, and the module is registered with
  `Flowgent__Modules__0__Id=support` (see [`deploy/README.md`](../deploy/README.md)).
- The workflow reaches its tool through the label `support`, mapped to `http://support-mcp:8080`
  with `Flowgent__McpServers__0__*`.
- The gateway forwards to `http://engine:8080`; the dashboard forwards to
  `http://gateway:8080` (`GATEWAY_URL`).
- The images ship an `appsettings.json` that already lists `support` at index 0, so the compose
  file overrides index 0 instead of adding a new entry.

## Using a real model

```bash
FLOWGENT_LLM_PROVIDER=OpenAI FLOWGENT_LLM_APIKEY=<key> docker compose up --build
```

To use another OpenAI-compatible server, also set `Flowgent__Llm__Endpoint` and
`Flowgent__Llm__ModelName` on the `engine` service.

## Adding your own module

1. Put its definition folder under `/definitions/<id>` with another `volumes:` entry.
2. Register it with `Flowgent__Modules__<n>__Id=<id>`.
3. If it needs domain tools, add an MCP server service and map its label with
   `Flowgent__McpServers__<n>__Label` and `__Url`.

## Known limits

- The gateway image still lists a `support` module at `localhost:5082`. There is no module
  service in this stack, so `/api/modules/support/**` answers `503`. The dashboard does not use it.
- Engine state (runs, events, inbox) is in memory and is lost on restart.
