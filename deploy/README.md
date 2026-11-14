# Flowgent container images

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
  - `Flowgent__Definitions__Path` — where workflow definitions are loaded from.
  - `Flowgent__McpServers__*` — MCP server endpoints the engine reaches for domain tools.

```bash
docker run --rm -p 8080:8080 -e Flowgent__Llm__Provider=Demo flowgent-engine:local
```

### gateway (core platform)

- Port: `8080`.
- Key env vars:
  - `Gateway__Engine__Address` — address of the engine the gateway forwards to.

```bash
docker run --rm -p 8080:8080 -e Gateway__Engine__Address=http://engine:8080 flowgent-gateway:local
```

### dashboard (core platform)

- Port: `80` (nginx serving the built SPA).
- nginx uses a minimal SPA fallback (`try_files $uri /index.html`). The gateway
  reverse-proxy block is intentionally **not** wired this round (no compose
  networking yet); the SPA talks to the gateway via its own configuration.

```bash
docker run --rm -p 8080:80 flowgent-dashboard:local
```

### support-mcp (example domain module — not part of the core platform)

- Port: `8080`.
- Key env vars:
  - `SUPPORT_DUMMYAPPS_ROOT=/app/dummy-apps` — root the `locateComponent` tool
    searches. The runtime image already copies `modules/support/dummy-apps`
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

The first intended release tag is **`v0.1.0-alpha.1`**. Do not create the git
tag as part of this change; tag the release when ready and CI will publish the
versioned images.

## Scope / next round

This round covers images and CI publishing only. Compose files, local
end-to-end wiring, and a reverse-proxy block that puts the dashboard behind the
gateway are deliberately deferred to a later round.
