# Support demo responses

[← Support module](../README.md) · [Pipeline](pipeline.md) · [Platform: Demo mode](../../../docs/demo-mode.md)

In Demo mode (`Flowgent:Llm:Provider = Demo`, the default) the three agent steps (`classify`, `resolve`, `generate_code`) answer from the module's demo file [`demo/support-email.yaml`](../definition/demo/support-email.yaml), referenced by `workflows[].demo` in `module.yaml`. Responses are deterministic and all generated texts start with `[Demo]`. Routing, agent assignment and the component lookup are not simulated: they run exactly as with a real model.

## Classification (`classify`)

Rules on `subject + " " + body`, case-insensitive, first match wins:

| Pattern | Category | Confidence |
|---------|----------|------------|
| `\btests?\b`, `flaky`, `coverage` | `QualityTestIssue` | 0.88 |
| `frontend`, `front-end`, `\.razor`, `component`, `\bcss\b`, `\bui\b`, `button`, `render`, `display` | `FrontendBug` | 0.90 |
| `exception`, `error`, `crash`, `\bnull\b`, `\bsql\b`, `\bapi\b`, `endpoint`, `database`, `\bbug\b`, `latency`, `controller`, `injection`, `timeout` | `BackendBug` | 0.92 |
| none | `OutOfScope` (`isCodeRelated: false`) | 0.95 |

## Root cause analysis (`resolve`)

| Field | Demo value |
|-------|------------|
| `rootCauseDescription` | `[Demo] Simulated root cause analysis for "<subject>": the defect originates in <component>.` |
| `affectedComponent` | The component found by `locate_component` (e.g. `OrderController.cs`, or `UnknownComponent`) |
| `severityAssessment` | `Critical` if the prompt (subject, body, component) contains `injection` or `security`, `High` for `exception` or `crash`, otherwise `Medium` |
| `proposedFixSummary` | `[Demo] Apply a targeted fix in <component> and add a regression test.` |
| `requiresEscalation` | `false` |

## Code generation (`generate_code`)

| Field | Demo value |
|-------|------------|
| `title` | `[Demo] Fix <component>` |
| `description` | The proposed fix |
| `affectedFilePaths` | `[<path from locate_component>]`, e.g. `dummy-apps/application-a/src/Controllers/OrderController.cs` |
| `simulatedDiff` | A small unified diff of that path with a `[Demo] simulated change` comment line |

## Limitations

- The demo root cause and diff do not read the dummy-app code; with a real model the prompts include the located file content.
- Keyword classification can differ from a real model on unusual wording.
