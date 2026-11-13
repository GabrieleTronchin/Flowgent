# Expressions

[← README](../README.md) · [Workflow reference](workflow-reference.md)

Workflows are MAF declarative documents, so expressions inside them are **PowerFx** (the declarative workflow builder's expression language), not Scriban. An expression is written `=Expr` as an action value (e.g. a `SetVariable` `value`, an `InvokeMcpTool` argument, a `ConditionGroup` `condition`). `SendActivity`/`Question` text uses `{expr}` interpolation inside a string, not a bare `=expr`.

> Scriban (`{{ … }}`) is gone with the custom workflow format. The one remaining non-PowerFx template is the manifest `title`, which uses a tiny `{field}` interpolation over the input object (e.g. `title: "{subject}"`).

## Reading data

The run's intake input arrives as the trigger message text (a JSON string). PowerFx has no `ParseJSON`, so a workflow reads structured data through the seam's built-in MCP tools and then `First(...)`:

```yaml
- kind: InvokeMcpTool
  id: read_input
  serverUrl: inproc://flowgent
  serverLabel: flowgent
  toolName: flowgent.input          # echoes the run input as a JSON-object string
  requireApproval: false
  arguments: { payload: =System.LastMessage.Text }
  output: { result: Local.intake }
# then: =First(Local.intake).subject  /  =First(Local.intake).body
```

`flowgent.parse` does the same for any JSON string (for example an agent's structured answer, `=MessageText(Local.someAgentMessage)`), so `=First(Local.parsed).field` reads a field out of it.

| Source | How to read it |
|--------|----------------|
| Intake input | `flowgent.input` → `First(Local.intake).<field>` |
| An agent's structured answer | `flowgent.parse` on `MessageText(Local.msg)` → `First(Local.obj).<field>` |
| A module tool result | the `output.result` local → `First(Local.result).<field>` |
| A prior step's snapshot | it was written to `Global.<stepId>`; read it back as a `Local` via `flowgent.parse` where needed |

## Common PowerFx

| Need | Example |
|------|---------|
| Build text | `=Concatenate(First(Local.intake).subject, " ", First(Local.intake).body)` |
| Lower-case + newline | `=Lower(...)`, `Char(10)` for `\n` |
| Keyword test (no regex) | `=Find("application a", Local.rtText) > 0` — `IsMatch` is not supported, lower-case every literal |
| Boolean logic | `=And(Local.matchA, Local.matchB)`, `=Or(...)`, `=Not(...)` |
| Choose | `=If(Local.team = "Alpha", "Writer", "Reviewer")`, `=Switch(First(Local.obj).category, "BackendBug", "BackendDeveloper", …, "")` |
| Default/fallback | `=Coalesce(First(Local.approval).reason, "Rejected")` |
| Number → string | `=Text(First(Local.obj).confidenceScore * 100, "0")` |
| Record literal | `={ team: Local.team, role: Local.role }` |

## Conditions and branches

`ConditionGroup` evaluates its `conditions[]` in order (first true wins) and otherwise runs `elseActions`. Each condition's actions typically `SetVariable` and/or `GotoAction` to an action id:

```yaml
- kind: ConditionGroup
  id: after_classify
  conditions:
    - condition: =First(Local.classifyObj).isCodeRelated = true
      id: is_code
      actions:
        - { kind: GotoAction, id: goto_route, actionId: rt_text }
  elseActions:
    - { kind: GotoAction, id: goto_oos, actionId: classified_out_of_scope_message }
```

## Crossing the seam boundary

Values cross between the engine and PowerFx as JSON strings. Two invariants to respect when authoring:

- A module tool result is returned as exactly one non-numeric JSON string; a bare top-level number is wrapped (a bare number crashes the `InvokeMcpTool` PowerFx binding).
- Each step writes its output to `Global.<stepId>` (snapshot) so the engine can surface it as `steps.<stepId>`; terminals write `Global.__terminalId` / `Global.__message` / `Global.__finalOutput`.

See the [workflow reference](workflow-reference.md) for the full run contract, and [`modules/support/definition/workflows/support-email.yaml`](../modules/support/definition/workflows/support-email.yaml) for a worked example.
