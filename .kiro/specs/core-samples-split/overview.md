# Core / samples split — overview

Goal: make the repository a generic platform (core) with the example modules clearly separated
(samples), so that people who use the product through the container images see a generic product
and the examples can later move to their own repository with little work. The documentation
restructure comes last, once the layout is final.

## Two audiences

| | Uses the product | Contributes |
|---|---|---|
| How | Container images, own definition folders, optional MCP server | Visual Studio and source code |
| Docs | Root `README.md`, `deploy/`, `compose/`, how-to guides | `CONTRIBUTING.md`, architecture and technical docs |

The root README is for users. Anything about building from source, the repository tree and tests
moves to `CONTRIBUTING.md` and the contributor docs. Documentation is English only, no images for now.

## Decisions taken

1. **Samples are examples, not the product.** `jokes`, `support` and their MCP server go to a
   top-level `samples/` folder, in the same repository for now. The module **template** is part of
   the core and lives in `backend/template/`.
2. **Two solutions.** `backend/Flowgent.Core.slnx` (engine, gateway, shared, core AppHost, tests,
   template) and `samples/Flowgent.Samples.slnx` (core + samples). The core solution must build and
   pass its tests without any sample.
3. **Build properties at the repository root.** `Directory.Build.props`, `Directory.Packages.props`
   and `.editorconfig` move from `backend/` to the root so `backend/` and `samples/` inherit them.
   The .NET Dockerfiles (engine, gateway, support-mcp) copy them from the root.
4. **Two AppHosts.** `backend/host` runs engine, gateway, dashboard and the template.
   `samples/host` also wires the samples.
5. **Shared AppHost wiring library.** `ModuleWiring` moves to a small packable library used by both
   AppHosts (project reference now). Publishing it as a NuGet package is wanted but later: it needs
   a feed decision (nuget.org or GitHub Packages) and CI packing. The same applies to the packages the
   future samples repository will need (`Module.Sdk`, `Contracts`, `Definitions`, `ServiceDefaults`).
6. **Demo responses are declared by each module**, not hard-coded in the engine. Today
   `DemoResponseProvider` knows `support-email`, `joke`, `template-hello`, `fixture-flow`, `hello`
   and `review`: the engine is not generic and a user's own workflow cannot run in Demo mode.
7. **CI has two jobs:** one builds and tests the core solution alone (proves the core stands alone),
   one builds and tests the samples solution.
8. **Frontend stays as is.** The dashboard is generic. Only test fixtures use `support-email` as a
   string; renaming them is optional cleanup.
9. **Documentation last**, after the layout is final (see `tasks.md`, PR 5).

## Demo file format (module-declared)

- Optional. A module without a demo file works normally with a real model. In Demo mode, a step
  with no response fails with a message saying where to add it
  (`demo/<workflow>.yaml` of module `<id>`); there is no generic fallback because the next step
  expects a JSON with specific fields.
- One file per workflow, `demo/<workflow>.yaml`, referenced from `module.yaml`, validated by a schema.
- Per step: `extract` (named regexes over the rendered prompt), ordered `rules` (`when` regex on
  the prompt, `output` object), `default` output. Strings in `output` accept `{name}` placeholders
  filled from the extracted values.
- Dynamic C# logic is simplified: severity keywords become several rules, the 80 character truncation
  is dropped. Small differences in demo texts are accepted and tests adapt.
- Engine test workflows (`hello`, `review`, `fixture-flow`) get their demo files in the test
  definitions. The template includes a demo example.

## Target layout

```
Directory.Build.props, Directory.Packages.props, .editorconfig, global.json, nuget.config
backend/
  Flowgent.Core.slnx
  host/                 # core AppHost
  engine/ gateway/ shared/ tests/
  template/             # module template, with a demo example
samples/
  Flowgent.Samples.slnx
  host/                 # samples AppHost
  jokes/ support/       # support: service, mcp, tests, dummy-apps, docs
compose/                # generic stack + samples overlays
```

## Impact (estimate, not measured)

- About 80 files moved with `git mv` (history kept), plus the template moving into `backend/`; 40-50
  more files edited (solutions, project references, AppHost, tests, CI, Dockerfiles, docs, steering).
- `dotnet test --solution backend/Flowgent.slnx` no longer exists; contributors open the core
  solution in Visual Studio and the samples solution for the examples.
- Support-specific tests (parity, regression, `MultiModuleIsolationTests`, the support rules in the
  architecture tests) move to the samples solution. Platform tests keep only the fixture module, a
  fixture MCP server and the template.
- Risk is medium: mostly mechanical, but it touches CI and Dockerfiles. Docker is not available on the
  author's machine, so Docker changes are verified by the pipeline.

## Open points and unknowns

- The test harness finds the repository root by looking for `Flowgent.slnx`. With two solutions, a
  sample test would find `samples/Flowgent.Samples.slnx` first. The marker must change.
- Whether the demo file fits the module loader, the version hash and the envelope schema without
  other changes; whether step ids in the demo file can be validated against the workflow (the
  projector reads lines, it does not parse the YAML).
- Whether `ModuleWiring` works as a separate library (`AddProject<TProject>` uses the generated
  `Projects.*` types of the host; the generic constraint should allow it, not checked).
- Which feed to publish NuGet packages to.
- D2: the future of the Module SDK (affects what the samples repository needs).
- Not related but pending: build and run the compose stack with Docker (4.2/4.3), the
  `v0.1.0-alpha.1` release tag, the "how to add a module via MCP" guide (3.2).
