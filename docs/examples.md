# Examples

[← README](../README.md) · [Documentation](README.md)

The example modules show how to use the platform; they are not part of it. They live in `samples/` and may move
to a repository of their own. This is the only page that lists them.

| Example | What it shows | Folder |
|---------|---------------|--------|
| Jokes | The smallest module: one agent step, no tools, a demo file. Start here. | [`samples/jokes`](../samples/jokes/definition/module.yaml) |
| Support | A complete case: classification, keyword routing, an MCP tool (`locateComponent`), human approval, failure terminals and ten scenarios. | [`samples/support`](../samples/support/README.md) |

The module **template** is not an example but the starting point for a new module: [`backend/template`](../backend/template/README.md).

## Run them

- With Docker: the overlays in [`compose/samples/`](../compose/README.md#try-it-with-a-sample).
- From source: `dotnet run --project samples/host` (see [Orchestration](contributing/orchestration.md)).
