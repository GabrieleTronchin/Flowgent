# Docker Compose

> Not tested yet: these files were prepared without Docker available, and the GHCR images are published
> only from the first release tag (`v0.1.0-alpha.1`, not created yet). Until then build the images from
> source (see [Build from source](#build-from-source)). Run it once and fix what it turns up before
> relying on it.

`docker-compose.yml` is the platform alone: engine, gateway and dashboard, in Demo mode (no API key). It
contains no module. The `samples/` overlays add the example modules of this repository.

| Service | Role | Host port |
|---------|------|-----------|
| `dashboard` | React console (nginx); forwards `/api` and the run stream to the gateway | 5173 |
| `gateway` | Single entry point (YARP) | 5080 |
| `engine` | Runs the workflows; reads module definitions from `./definitions` (mounted at `/definitions`) | none |

## Try it with a sample

```bash
cd compose
docker compose -f docker-compose.yml -f samples/jokes.yml up
```

Open http://localhost:5173, pick the `Jokes` module, write a topic and submit. Stop with `docker compose down`
(repeat the same `-f` options).

| Overlay | Adds |
|---------|------|
| `samples/jokes.yml` | The jokes module (one agent step, no tools). |
| `samples/support.yml` | The support module and its MCP server `support-mcp` (`locateComponent` tool). |

Overlays can be combined: `-f docker-compose.yml -f samples/jokes.yml -f samples/support.yml`. They mount
definition folders from `../samples/`, so they need this repository.

## Your own module

1. Put the definition folder in `definitions/<id>/` (see [`definitions/README.md`](definitions/README.md)).
2. Register it on the engine, in an override file of your own (for example `docker-compose.override.yml`,
   which `docker compose up` reads automatically):

   ```yaml
   services:
     engine:
       environment:
         Flowgent__Modules__0__Id: notes
   ```

3. If its workflows call domain tools, add the MCP server as a service and map the workflow `serverLabel`:

   ```yaml
   services:
     engine:
       environment:
         Flowgent__McpServers__0__Label: notes
         Flowgent__McpServers__0__Url: http://notes-mcp:8080
     notes-mcp:
       image: <your MCP server image>
   ```

Changes to the files under `definitions/` are picked up without restarting (the engine checks the folder every
5 seconds). Details of the engine image: [`deploy/README.md`](../deploy/README.md).

## Build from source

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml -f samples/jokes.yml up --build
```

`docker-compose.build.yml` builds engine, gateway and dashboard from this repository. With the support sample,
also add `samples/support.build.yml`.

## Settings

| Variable | Default | Effect |
|----------|---------|--------|
| `FLOWGENT_TAG` | `alpha` | Tag of the GHCR images. |
| `FLOWGENT_LLM_PROVIDER` | `Demo` | `OpenAI` uses a real model (any OpenAI-compatible API). |
| `FLOWGENT_LLM_APIKEY` | empty | API key, required with `OpenAI`. |

```bash
FLOWGENT_LLM_PROVIDER=OpenAI FLOWGENT_LLM_APIKEY=<key> docker compose -f docker-compose.yml -f samples/jokes.yml up
```

For another OpenAI-compatible server, also set `Flowgent__Llm__Endpoint` and `Flowgent__Llm__ModelName` on the
`engine` service.

## How it fits together

- The images contain no modules: the engine knows a module only when it is registered by id
  (`Flowgent__Modules__<n>__Id`) and its folder exists under `/definitions`.
- The overlays use fixed indexes so they combine: jokes is module 0, support is module 1 and MCP server 0.
  Your own override should use other indexes if you combine it with them.
- `./definitions` is mounted read-write so the overlays can mount their folders inside it; the engine never
  writes to it.
- The gateway forwards to `http://engine:8080`; the dashboard forwards to `http://gateway:8080` (`GATEWAY_URL`).

## Known limits

- Engine state (runs, events, inbox) is in memory and is lost on restart.
