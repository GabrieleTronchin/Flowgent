# Getting started

[← README](../README.md) · [Documentation](README.md)

This page runs the platform with Docker, sends a first workflow, approves a step and then switches to a real
model. It takes about ten minutes, most of it the first image build.

> The GHCR images are published from the first release tag (`v0.1.0-alpha.1`, not created yet). Until then the
> commands below build them from this repository with `docker-compose.build.yml`.

## 1. Start the platform with a sample

```bash
git clone https://github.com/GabrieleTronchin/Flowgent.git
cd Flowgent/compose
docker compose -f docker-compose.yml -f docker-compose.build.yml -f samples/jokes.yml up --build
```

This starts three containers, in Demo mode (no model, no API key):

| Container | What it is | Address |
|-----------|------------|---------|
| `dashboard` | The console | http://localhost:5173 |
| `gateway` | The single API entry point | http://localhost:5080 |
| `engine` | Runs the workflows; reads module definitions from `compose/definitions/` and the sample overlays | not published |

## 2. Run a workflow

1. Open http://localhost:5173.
2. In the header, pick the module **Jokes** and the workflow **Tell a joke**. The graph shows its steps.
3. Under **New input**, pick the template **About cats** (or type a topic) and press **Submit**.
4. The run appears in **Runs**: it goes through the `tell` step and ends in `done` with a demo joke.
   **Event log** lists every transition.

In Demo mode the answer comes from the module's demo file, `samples/jokes/definition/demo/joke.yaml`.

## 3. Approve a step

The support sample pauses for a human decision. Stop the stack (`Ctrl+C`, then `docker compose down` with the
same `-f` options) and start it with the support overlay as well:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml \
  -f samples/jokes.yml -f samples/support.yml -f samples/support.build.yml up --build
```

1. Pick the module **Support** and submit one of its templates (for example a scenario of application A).
2. The run stops at `approval` with status **AwaitingInput**.
3. Open **Approvals**, read the proposed fix and press **Approve** (or **Reject** with a reason).
4. The run continues and ends with a simulated code change.

## 4. Use a real model

Any OpenAI-compatible chat API works:

```bash
FLOWGENT_LLM_PROVIDER=OpenAI FLOWGENT_LLM_APIKEY=<your key> \
  docker compose -f docker-compose.yml -f docker-compose.build.yml -f samples/jokes.yml up
```

For another provider (Groq, Ollama and others) also set `Flowgent__Llm__Endpoint` and `Flowgent__Llm__ModelName`
on the engine. Details: [Demo mode and LLM providers](guides/demo-mode.md).

## Next steps

- Write your own module: [Writing modules](guides/writing-modules.md) walks through a complete one, placed in
  `compose/definitions/<id>/`.
- Learn the workflow format: [Writing workflows](guides/writing-workflows.md).
- Deploy the images elsewhere: [Deployment](guides/deployment.md).
