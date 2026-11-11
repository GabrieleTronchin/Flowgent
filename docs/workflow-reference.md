# Workflow and module reference

[← README](../README.md) · [Expressions](expressions.md) · [Module authoring](module-authoring.md) · [Demo mode](demo-mode.md)

A module definition is a folder of YAML, Markdown and JSON files (`modules/<id>/definition/`) published by the module service. Workflows are **Microsoft Agent Framework (MAF) declarative workflows**: the engine feeds each workflow's YAML to the declarative workflow builder and runs it, so the workflow format is MAF's, not a Flowgent-specific one. Flowgent owns only the **module envelope** (the manifest and samples) and the **run contract** the workflow follows (how it reads intake input, snapshots step outputs, and marks terminals).

Two envelope formats have a JSON Schema in `backend/schemas/` (served by `GET /api/engine/schemas/{module|samples}`):

| File | Schema | Content |
|------|--------|---------|
| `module.yaml` | `module.schema.json` | Module id, config, agents, tools, workflow **descriptors** and sample files |
| `samples/*.yaml` | `samples.schema.json` | Example inputs shown by the dashboard |
| `workflows/*.yaml` | (none) | One MAF declarative workflow. Not step-validated at load; the engine **trial-builds** it with the declarative builder and reports a build failure as a module error. |

> The old custom workflow format (`workflow.schema.json`, `kind: llm/rule/map/tool/http/human-input/terminal`, Scriban `{{ … }}` templates) was removed when the engine moved to MAF declarative. `/api/engine/schemas/workflow` no longer exists.

The complete, runnable example is the template module: [`modules/_template/definition`](../modules/_template/definition). The support module ([`modules/support/definition`](../modules/support/definition)) exercises every action kind.

## Module manifest (`module.yaml`)

```yaml
# yaml-language-server: $schema=../../../backend/schemas/module.schema.json
id: notes
name: Notes
description: Summarises notes.
config:                       # free-form, available as config.* in templates; never secrets
  maxSummaryWords: 30
agents:                       # used for attribution, status and the agent the workflow addresses
  - id: notes_writer
    name: Writer
    group: Writers            # optional grouping shown by the dashboard
    role: Writer              # optional
    persona: A concise technical writer.
tools:                        # operations implemented by the module service, reached via InvokeMcpTool
  - name: notes.wordCount
    description: Counts the words of a text.
    input:  { schema: { file: schemas/word-count.input.schema.json } }
    output: { schema: { file: schemas/word-count.output.schema.json } }
    timeoutSeconds: 10        # optional
workflows:                    # descriptor list (the dashboard/intake metadata lives here, not in the workflow YAML)
  - id: notes-summary
    file: workflows/notes-summary.yaml
    version: 1                # integer; bump when the workflow changes
    name: Notes summary
    description: Summarises a note and counts its words.
    title: "{text}"           # run title; a simple {field} interpolation over the input object
    input:
      schema: { file: schemas/notes-summary.input.schema.json }
    errorMessage: Text is required.          # detail of the 400 response (optional)
    terminals:                # every terminal the workflow can reach, with its outcome
      - { id: done, outcome: success }
      - { id: rejected, outcome: failure }
samples:
  - samples/notes-summary.samples.yaml
```

Required: `id`, `name`, `workflows`. Tool names must start with the module id. Each `workflows[]` entry is a **descriptor**: id, file, integer `version`, name, optional `description`/`title`, the input `schema`, optional `errorMessage`, and `terminals[]` (`{ id, outcome }` with outcome `success` or `failure`). The engine reads graph/terminal metadata from here — the workflow YAML is opaque to the loader.

## Workflow definition (MAF declarative)

A workflow is a MAF declarative document: a `Workflow` with an `OnConversationStart` trigger whose `actions` list runs top to bottom. The run's intake input arrives as the trigger message text (a JSON string). Authoring conventions the engine relies on:

- **Read intake input**: PowerFx has no `ParseJSON`, so a workflow reads its input through the seam's built-in MCP tool `flowgent.input` (serverLabel `flowgent`), whose JSON-object-string result is read as `First(Local.x).<field>`. Use `flowgent.parse` the same way to turn any JSON string (e.g. an agent's structured answer) into a record.
- **Step output snapshot**: each logical step writes its output to `Global.<stepId>` via a trailing `SetVariable` named `<stepId>_snapshot`. The seam reads these from run state and surfaces them as `steps.<stepId>` and as StepCompleted events (the dashboard graph shows exactly these snapshot steps). An optional sibling `Global.<stepId>__detail` string becomes the step's detail.
- **Assigned agent**: writing `Global.assignedAgentId` sets the run's assigned agent (telemetry, dashboard badge).
- **Terminals**: a terminal writes the reserved globals `Global.__terminalId` (the terminal id, matching a manifest `terminals[]` entry), `Global.__message` (the run detail), and, for a success output, `Global.__finalOutput` (the structured JSON exposed as the run's final output). Follow it with `EndConversation` so the action list does not fall through into the next terminal.

```yaml
kind: Workflow
trigger:
  kind: OnConversationStart
  id: notes-summary
  actions:
    - kind: InvokeMcpTool          # read intake input as a record
      id: read_input
      serverUrl: inproc://flowgent
      serverLabel: flowgent
      toolName: flowgent.input
      requireApproval: false
      arguments: { payload: =System.LastMessage.Text }
      output: { result: Local.intake }
    # … agent / tool / branch / terminal actions …
```

### Action kinds

| MAF action | Role (old step kind) |
|------------|----------------------|
| `InvokeAzureAgent` | LLM call (`llm`). Resolved by the engine's agent provider over its `IChatClient`, not Azure. `agent.name` carries `"<stepId>\|<agentId>"` so the provider keys Demo responses/telemetry by step id and resolves the agent by id. The user prompt is authored; there is no per-step system prompt / temperature / output-schema on the call — validate the returned JSON downstream (e.g. read `First(parse(...)).field`). |
| `InvokeMcpTool` | Module tool call (`tool`), the built-in `flowgent.input`/`flowgent.parse`, and — since the declarative HTTP action is not yet used — any external-API bridge exposed as a module tool. `serverLabel` → module id, `toolName` → tool. The result crosses as one non-numeric JSON string (a bare top-level number is wrapped). |
| `SetVariable` | Computed values and lookups (`map`), plus the snapshot/detail/terminal plumbing. Uses PowerFx (`=Expr`). |
| `ConditionGroup` | Branching and rule routing (`rule`). Ordered `conditions[]` (first true wins) + `elseActions`; branches use `GotoAction` to jump to an action id. |
| `Question` | Human-input gate (`human-input`). Pairs with a `ConditionGroup` on the resumed answer. See [Human input](human-input.md). |
| `SendActivity` | Terminal activity (`terminal`); the terminal's outcome/message/output come from the manifest + reserved globals above. |

`GotoAction { actionId }` targets an exact action id (e.g. a terminal's first `SetVariable`). A `GotoAction` to a non-existent id makes the trial build fail, which marks the module invalid.

### Routing without regex (PowerFx)

PowerFx `IsMatch` is recognised but not supported in this builder, so keyword routing uses `Lower(...)` + `Find(...)`, with every literal lower-cased:

```yaml
- kind: SetVariable
  id: rt_text
  variable: Local.rtText
  value: =Lower(Concatenate(First(Local.intake).subject, " ", First(Local.intake).body))
- kind: SetVariable
  id: rt_a
  variable: Local.matchA
  value: =Or(Find("application a", Local.rtText) > 0, Find("applicationa", Local.rtText) > 0)
```

## Demo mode

Workflows carry **no** `demo:` block. In Demo mode the engine's `DemoResponseProvider` supplies each agent step's response, keyed by `(workflowId, stepId)`. Add a key there when you add an agent step. See [Demo mode](demo-mode.md).

## Samples

```yaml
# yaml-language-server: $schema=../../../../backend/schemas/samples.schema.json
samples:
  - id: notes-short
    name: Short note
    group: Examples              # optional, groups the dashboard picker
    workflow: notes-summary
    description: optional
    input: { text: "Agent Framework workflows in YAML." }
  - id: notes-empty
    name: Empty note (validation)
    workflow: notes-summary
    expectInvalid: true          # the input intentionally fails the input schema
    input: { text: "   " }
```

Samples are validated against the workflow input schema unless `expectInvalid: true`.

## Input schemas and the dashboard form

The dashboard builds the submission form from the workflow input schema: `string` → text input (`x-multiline: true` → textarea), `enum` → select, `boolean` → checkbox, `number`/`integer` → number input. `title` and `description` label the fields.

## Validation

The engine validates a module when it loads it, and the Module SDK runs the same checks at module startup and in tests (`ModuleDefinitionAssert.IsValid`). Validation now has two layers:

- **Envelope** (the manifest and samples): YAML syntax and format-schema violations; duplicate workflow/agent/tool/sample ids; tool names without the module prefix; missing or invalid referenced schema files; samples referencing an unknown workflow or not matching the input schema.
- **Trial build** (the declarative workflow): the engine builds each workflow's YAML with a no-op provider/handler; a build failure (malformed document, a `GotoAction` to a non-existent action, etc.) is reported as a module error naming the workflow file. The declarative builder gives no step id or line, so those are not reported (a change from the old custom step-graph validator).

An invalid module is not loaded; other modules are unaffected. Errors are shown by `GET /api/engine/modules` and the dashboard.
