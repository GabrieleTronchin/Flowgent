# Flowgent container images

> ⚠️ **Beta / pre-release.** These images are early (`v0.1.0-alpha.1`) and not
> production-ready. Contents, environment variables and defaults can change
> between releases. Use the `alpha` channel tag for the latest pre-release or a
> pinned `0.1.0-alpha.1` tag for reproducibility.

Four multi-stage Dockerfiles that package the Flowgent services. **The build
context is the repository root** for every image, so always build from the repo
root and pass the Dockerfile with `-f`:

```bash
docker build -f deploy/engine.Dockerfile      -t flowgent-engine:local .
docker build -f deploy/gateway.Dockerfile     -t flowgent-gateway:local .
docker build -f deploy/dashboard.Dockerfile   -t flowgent-dashboard:local .
docker build -f deploy/support-mcp.Dockerfile -t flowgent-support-mcp:local .
```

Each `.NET` build stage copies the repo-root `nuget.config` (nuget.org-only
feed) first, then restores and publishes the target service. A per-image
`*.Dockerfile.dockerignore` keeps the build context small (excludes `bin/`,
`obj/`, `node_modules/`, `.git/`, `.worktrees/`, `.agents/`, `artifacts/` and
the test projects).

## Core platform vs example module

Flowgent's core platform is **domain-agnostic and reusable**: the engine runs
YAML workflows and reaches domain tools over MCP, the gateway is the single
ingress, and the dashboard monitors any module. A **domain module is an example
of how to extend the platform, not part of the product** — the support MCP
server is shipped as such an example.

| Image | Category | Port | What it is |
|-------|----------|------|------------|
| `flowgent-engine` | **Core platform** | 8080 | Domain-agnostic workflow runtime (MAF declarative) that runs YAML workflows and reaches domain tools over MCP. |
| `flowgent-gateway` | **Core platform** | 8080 | Single ingress (YARP) for the dashboard and clients. |
| `flowgent-dashboard` | **Core platform** | 80 | Generic React console for monitoring any module (static SPA served by nginx). |
| `flowgent-support-mcp` | **Example domain module** (not part of the core platform) | 8080 | Example MCP server: the support demo's `locateComponent` domain tool over Streamable HTTP. Shows how to write a domain module. |

## Images

### engine (core platform)

- Port: `8080` (`ASPNETCORE_HTTP_PORTS=8080`).
- Key env vars:
  - `Flowgent__Llm__Provider=Demo` — LLM provider (Demo needs no API key).
  - `Flowgent__Definitions__Path=/definitions` — where workflow definitions are loaded from (set by the image).
  - `Flowgent__Modules__<n>__Id` — registers a module by id (see below).
  - `Flowgent__McpServers__*` — MCP server endpoints the engine reaches for domain tools.

```bash
docker run --rm -p 8080:8080 -e Flowgent__Llm__Provider=Demo flowgent-engine:local
```

#### Providing workflow definitions

The image contains **no** workflow definitions and is never rebuilt per application.
Mount one folder per module under `/definitions`; the folder name is the module id:

```
/definitions/<moduleId>/
├── module.yaml
├── workflows/    # MAF declarative YAML
├── prompts/
├── schemas/
└── samples/
```

Register each module by id. A module read from the volume needs **no** `Url`
(no module service is involved):

```bash
docker run --rm -p 8080:8080 \
  -e Flowgent__Llm__Provider=Demo \
  -e Flowgent__Modules__0__Id=notes \
  -v ./my-module/definition:/definitions/notes:ro \
  flowgent-engine:local
```

- The engine checks the folder every `RefreshSeconds` (default 5); a changed file is loaded
  without restarting, and runs already in progress keep the definition they started with.
- If a module has no folder under `/definitions`, the engine falls back to pulling its package
  from the module service (`Flowgent__Modules__<n>__Url`, `GET /_module/package`).
- A module listed in `Flowgent__Modules__*` must be registered explicitly; folders under
  `/definitions` that are not registered are ignored.
- Domain tools are reached over MCP: map each `serverLabel` with
  `Flowgent__McpServers__<n>__Label` and `__Url`.

### gateway (core platform)

- Port: `8080`.
- Key env vars:
  - `Gateway__Engine__Address` — address of the engine the gateway forwards to.

```bash
docker run --rm -p 8080:8080 -e Gateway__Engine__Address=http://engine:8080 flowgent-gateway:local
```

### dashboard (core platform)

- Port: `80` (nginx serving the built SPA).
- Key env vars:
  - `GATEWAY_URL=http://gateway:8080` — where nginx forwards `/api/` and the
    `/flowgent.RunMonitor/` run stream (the SPA calls these same-origin paths).
  - `NGINX_RESOLVER=127.0.0.11` — DNS server used to find the gateway (Docker's embedded DNS).
- The gateway address is resolved on each request, so the container starts even if the
  gateway is not up yet.

```bash
docker run --rm -p 5173:80 -e GATEWAY_URL=http://host.docker.internal:5080 flowgent-dashboard:local
```

### support-mcp (example domain module — not part of the core platform)

- Port: `8080`.
- Key env vars:
  - `SUPPORT_DUMMYAPPS_ROOT=/app/dummy-apps` — root the `locateComponent` tool
    searches. The runtime image already copies `samples/support/dummy-apps`
    there and sets this default.

```bash
docker run --rm -p 8080:8080 flowgent-support-mcp:local
```

## Publishing (CI)

`.github/workflows/images.yml` publishes to GitHub Container Registry (GHCR) as
`ghcr.io/<owner>/flowgent-{engine,gateway,dashboard,support-mcp}`:

- Push a git tag `v*` → versioned semver tags (e.g. `v0.1.0-alpha.1` →
  `0.1.0-alpha.1`) plus a moving prerelease channel tag (`alpha`).
- Push to `master` → a floating `edge` tag.
- Pull requests → build only (validation), no push.

The first intended release tag is **`v0.1.0-alpha.1`**. It has not been created yet:
tag the release when ready and CI will publish the
versioned images.

## Reference stack

[`compose/`](../compose/README.md) runs the platform images (engine, gateway, dashboard) in Demo mode,
with an empty `definitions/` folder for your modules and overlays that add the samples (`jokes`,
`support` with `support-mcp`). It pulls the GHCR images or builds them with `docker-compose.build.yml`.
It is prepared but not yet run end to end.
