# Flowgent

![CI](https://github.com/GabrieleTronchin/Flowgent/actions/workflows/ci.yml/badge.svg)
![Dashboard CI](https://github.com/GabrieleTronchin/Flowgent/actions/workflows/dashboard-ci.yml/badge.svg)
![Status](https://img.shields.io/badge/status-beta-orange)
![Release](https://img.shields.io/badge/next%20release-v0.1.0--alpha.1-orange)
![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)
![License](https://img.shields.io/github/license/GabrieleTronchin/Flowgent)

**Run LLM workflows that you describe in YAML, plug in your own tools, and watch every run from a dashboard.**

> **Beta.** Flowgent works end to end, but formats, settings and APIs can still change. The container images
> are published from the first release (`v0.1.0-alpha.1`, not tagged yet); until then build them from source.

## What it does

You write a workflow as a YAML file: call a model, call a tool, branch, ask a person to approve, end. Flowgent
runs it, records every step and shows it live. The platform is the same for every use case; only your
workflow files and your tools change.

- **Configurable**: workflows are [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
  declarative YAML with PowerFx expressions. Add or change one without touching the platform.
- **Extensible**: your domain logic lives in your own tools, served over MCP (any language).
- **Observable**: a dashboard shows runs, steps, approvals, events and agents in real time.

It starts in **Demo mode**: no model and no API key; each agent step answers with the demo answers your module
declares. Switch to any OpenAI-compatible API when you are ready.

## Quick start

Requirements: Docker. From a clone of this repository:

```bash
cd compose
docker compose -f docker-compose.yml -f docker-compose.build.yml -f samples/jokes.yml up --build
```

Open http://localhost:5173, pick **Jokes**, write a topic and submit. The run goes through its steps and ends
with a (demo) joke. Once the images are published, drop `-f docker-compose.build.yml` and `--build`.

The step by step version, with an approval and a real model: [Getting started](docs/getting-started.md).

## How it fits together

```
Browser -> dashboard -> gateway -> engine -> your MCP servers (tools)
                                    |
                                    +-> your module definitions (YAML folder)
                                    +-> LLM (Demo or OpenAI-compatible)
```

Three images make the platform: `flowgent-engine`, `flowgent-gateway`, `flowgent-dashboard`. A **module** is a
folder of YAML files (workflows, prompts, schemas, samples, demo answers) mounted into the engine, plus,
if it needs them, an MCP server for its tools.

## Documentation

| I want to | Read |
|-----------|------|
| Run it and see a workflow | [Getting started](docs/getting-started.md) |
| Write a workflow | [Writing workflows](docs/guides/writing-workflows.md), [Expressions](docs/guides/expressions.md) |
| Write a module and its tools | [Writing modules](docs/guides/writing-modules.md) |
| Ask a person to approve a step | [Human input](docs/guides/human-input.md) |
| Run without a model, or with a real one | [Demo mode and LLM providers](docs/guides/demo-mode.md) |
| Deploy the images | [Deployment](docs/guides/deployment.md) |
| Look up a setting or an endpoint | [Configuration](docs/reference/configuration.md), [Engine API](docs/reference/engine-api.md) |
| See complete examples | [Examples](docs/examples.md) |
| Work on Flowgent itself | [Contributing](CONTRIBUTING.md) |

All documentation: [docs/](docs/README.md).

## License

MIT. See [LICENSE](LICENSE).
