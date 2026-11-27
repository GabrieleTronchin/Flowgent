# Core / samples split — tasks

Do them in order, one PR at a time, each from a new branch off `master` (deleted after the merge).
Each PR ends with a green build and the relevant tests. Show the diff before every commit.

- [x] **PR 1 — build properties to the repository root.** Done (`5b9ae21`). Move `Directory.Build.props`,
  `Directory.Packages.props` and `.editorconfig` from `backend/` to the root. In `engine.Dockerfile`,
  `gateway.Dockerfile` and `support-mcp.Dockerfile`, copy them from the root. Update `tech.md` and docs.
  Check: build, all tests, pipeline builds the images.
- [x] **PR 2 — demo responses declared by modules.** Done (`08c4db0`). Design and schema for `demo/<workflow>.yaml`;
  loader, version hash and validation; generic `DemoResponseProvider`; remove the hard-coded
  workflow ids from the engine; port the demo rules of `jokes`, `support`, `template`, fixture, and
  the engine test workflows; add a demo example to the template; update the demo docs.
  Check: engine tests, support regression tests, architecture tests.
- [x] **PR 3 — move the samples and split the solutions.** Done on `feat/samples-split`. The core
  integration tests use the fixture and the template; `MultiModuleIsolationTests` and the sample
  dependency rules moved to `samples/tests/Flowgent.Samples.Tests`. The core no longer has an
  MCP-over-HTTP integration test (the support MCP server is a sample); MCP routing stays covered by the
  engine's `ModuleToolHandlerTests` (in-memory MCP). `backend/modules/{jokes,support}` to
  `samples/`; `_template` to `backend/template/`; `Flowgent.Core.slnx` and `Flowgent.Samples.slnx`;
  core AppHost and `samples/host`; shared wiring library (packable); split architecture and
  integration tests; fix the repository root marker in the test harness; update `ci.yml` to two jobs;
  update `images.yml`, Dockerfile paths, `.dockerignore`, schema comments in `module.yaml`, `.http`
  files, steering and docs.
  Check: core solution builds and passes alone; samples solution passes; `docker compose config`.
- [x] **PR 4 — generic compose and sample overlays.** Done on `feat/compose-generic-stack`: `docker compose config`
  passes for the base file, each overlay, the combination and the build overrides; not run (no Docker).
  Overlays use fixed indexes (jokes module 0, support module 1 + MCP server 0); index gaps bind (`VolumeDefinitionTests`). `compose/docker-compose.yml` with engine, gateway
  and dashboard and an empty `definitions/` folder; `compose/samples/jokes.yml` and `support.yml`
  overlays; compose uses `ghcr.io/...:alpha` images with a build override for local builds.
  Not runnable until Docker is available; the pipeline checks the image builds.
- [x] **PR 5 — documentation restructure (last).** Done on `docs/restructure`: user README, getting started,
  examples page, guides/, reference/ (new configuration page), contributing/, CONTRIBUTING.md, Flowgent.Hosting README.
  The `notes` example of Writing modules was run on the engine (module Available, run Completed in Demo mode).
  Workflow snippets moved to block style: the declarative reader rejects inline mappings. Project READMEs kept (already uniform).
  - Root `README.md` for users: what it is, three key ideas, quick start with the images and the
    `jokes` sample, where to go next; short beta notice; link to `CONTRIBUTING.md`.
  - `CONTRIBUTING.md` and contributor docs: open the core solution in Visual Studio, run the AppHost,
    tests, architecture, repository tree, dependency rules, debugging.
  - Guides for users: write a workflow, write a module (definition folder + MCP server; the SDK is
    presented as advanced/contributor until D2 is decided), expressions, human input, Demo mode,
    configuration of the images, API reference.
  - Samples are described on a single "Examples" page so that moving them later is one edit.
  - Rewrite all 11 READMEs with the same short format; project READMEs stay 8-12 lines.
  - Check links between `.md` files after the rewrite.
  - Afterwards, optionally: a documentation site with VitePress on GitHub Pages (the repository is public).

## Later, not scheduled
- [ ] Decide D2 (Module SDK future).
- [ ] Publish NuGet packages (wiring library, `Module.Sdk`, `Contracts`, `Definitions`, `ServiceDefaults`).
- [ ] Move `samples/` to its own repository.
- [ ] Build and run the compose stack with Docker (4.2/4.3 of `maf-declarative-migration`).
- [ ] Tag `v0.1.0-alpha.1`.
- [ ] Guide "how to add a module via MCP" (3.2).
