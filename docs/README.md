# Flowgent documentation

[← README](../README.md)

## Use Flowgent

| Page | Content |
|------|---------|
| [Getting started](getting-started.md) | Run the platform with Docker, send a workflow, approve a step, use a real model |
| [Writing workflows](guides/writing-workflows.md) | `module.yaml`, the declarative workflow format, action kinds, samples, validation |
| [Expressions](guides/expressions.md) | PowerFx expressions and conditions |
| [Writing modules](guides/writing-modules.md) | A module folder, its tools on an MCP server, demo answers, the Module SDK (advanced) |
| [Human input](guides/human-input.md) | Approve, reject and abort |
| [Demo mode and LLM providers](guides/demo-mode.md) | Demo answers, OpenAI-compatible providers |
| [Deployment](guides/deployment.md) | The images, module definitions on a volume, Docker Compose |
| [Configuration](reference/configuration.md) | Every setting of the engine, gateway and dashboard |
| [Engine API](reference/engine-api.md) | `/api/engine` endpoints and the gRPC run stream |
| [Examples](examples.md) | The example modules of this repository |

## Work on Flowgent

Start with [CONTRIBUTING.md](../CONTRIBUTING.md).

| Page | Content |
|------|---------|
| [Architecture](contributing/architecture.md) | Services, module catalog, run model, persistence, repository layout, dependency rules |
| [Orchestration](contributing/orchestration.md) | The AppHosts, running services one by one, observability |
| [Gateway](contributing/gateway.md) | Routes, correlation id, 503 handling |
| [Inbox](contributing/inbox.md) | Asynchronous intake and run start |
| [Dashboard](contributing/dashboard.md) | Pages, graph, form, real-time updates |
| [Debugging](contributing/debugging.md) | `.http` files, troubleshooting, traces |
