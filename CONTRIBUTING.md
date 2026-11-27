# Contributing to Flowgent

This page is for working on Flowgent itself. To use it, start from the [README](README.md).

## Requirements

- .NET 10 SDK (pinned in `global.json`).
- Node.js 20.19 or later, for the dashboard.
- Visual Studio with .NET 10 support, or VS Code with the C# Dev Kit. Docker is optional (only for the images).

## The two solutions

| Solution | Contains | Use it for |
|----------|----------|------------|
| `backend/Flowgent.Core.slnx` | engine, gateway, shared libraries, `Flowgent.Hosting`, the module template, the core tests and the core AppHost | Work on the platform. It must build and pass its tests without `samples/`. |
| `samples/Flowgent.Samples.slnx` | the core projects plus the example modules (`jokes`, `support`), their tests and the samples AppHost | Work on the examples, or run everything together. |

Open the core solution in Visual Studio for platform work. In VS Code, `.vscode/` has build and test tasks and
launch configurations for both AppHosts and every service.

## First run

```bash
cd dashboard && npm install && cd ..
dotnet run --project backend/host       # engine, gateway, dashboard and the module template
dotnet run --project samples/host       # the same plus the example modules
```

| What | Where |
|------|-------|
| Dashboard | http://localhost:5173 |
| Gateway | http://localhost:5080 |
| Aspire dashboard (logs, traces, metrics) | http://localhost:15080 |

In Visual Studio, set `Flowgent.AppHost` (or `Flowgent.Samples.AppHost`) as the startup project. Everything runs in
Demo mode; to use a real model put the key in the git-ignored
`backend/engine/Flowgent.Engine.Service/appsettings.Development.json` (see [Demo mode](docs/guides/demo-mode.md)).
Running services one by one: [Orchestration](docs/contributing/orchestration.md).

## Build and test

```bash
dotnet build backend/Flowgent.Core.slnx
dotnet test --solution backend/Flowgent.Core.slnx
dotnet test --solution samples/Flowgent.Samples.slnx

cd dashboard
npm run typecheck
npm run test -- --run
npm run build
```

Tests run in-process with `Flowgent.Testing.PlatformHarness`: no ports, no Aspire, Demo mode. CI runs the core
solution alone (only `backend/` is checked out), the samples solution, and the dashboard. Pull requests also
build the four images.

## How the code is organised

- [Architecture](docs/contributing/architecture.md): services, module catalog, run model, persistence, repository
  layout and the dependency rules enforced by `Flowgent.Architecture.Tests`.
- Each main project has a short README: what it does, what is inside, what it may depend on
  (for example [`Flowgent.Engine.Core`](backend/engine/Flowgent.Engine.Core/README.md)).
- [Gateway](docs/contributing/gateway.md), [Inbox](docs/contributing/inbox.md),
  [Dashboard](docs/contributing/dashboard.md), [Debugging](docs/contributing/debugging.md).

## Rules

- **The core stays generic.** No core project references `samples/`; the engine and gateway contain no domain
  terms or workflow ids. Architecture tests check both.
- **Code style** comes from the root `.editorconfig`: file-scoped namespaces, `var` when the type is apparent,
  pattern matching, collection expressions, `_camelCase` private fields.
- **Expected failures use result types** (`IntakeResult`, `EngineSubmitResult`, step failures with a reason);
  exceptions are for unexpected errors. All I/O is async with a `CancellationToken`.
- **Hosts are factories**: every service exposes `XxxHost.Create(...)`, used by `Program.cs` and by the tests.
- **Packages** are versioned centrally in the root `Directory.Packages.props`; pin exact versions.
- **Documentation** is in English. Platform docs use generic examples; the example modules are listed only in
  [Examples](docs/examples.md) and documented in their own folders.
- Add or update tests with every change; regenerate checked-in snapshots with `UPDATE_SNAPSHOTS=1`.

## Pull requests

Branch from `master`, keep each pull request to one topic, and describe what changed and how you tested it. CI
must be green.
