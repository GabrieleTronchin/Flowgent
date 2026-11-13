# Phase 1 — Step mapping: our engine → MAF declarative

Gate for D3: confirm every step of `support` and `jokes` maps to MAF (declarative action / PowerFx / MCP) **before** deleting any engine code. Verified against MAF declarative APIs (microsoft/agent-framework, 1.27).

## Legend
- **PowerFx** = MAF declarative expression language (replaces Scriban).
- **Agent action** = `InvokeAzureAgent` YAML action, resolved by name through our `ResponseAgentProvider` (NOT Azure-bound; spike-proven).
- **MCP tool** = deterministic tool call via declarative `McpToolHandler` (D1b — to validate in the Phase 0 mini-spike).
- Structured LLM output: `ChatResponseFormat.ForJsonSchema(name, BinaryData.FromString(jsonSchema))` — schema from file, no .NET type. Confirmed supported.
- Human gate: MAF ships `QuestionExecutor` (declarative "ask"): emits `ExternalInputRequest`, checkpoints, yields to host, resumes via `CaptureResponseAsync`. Confirmed present.

## `jokes` (smoke test — trivial)
| our step | kind | MAF mapping | friction |
|---|---|---|---|
| `tell` | llm (+demo) | Agent action with structured output (`joke` schema) | demo response moves out of YAML into our IChatClient (see F1) |
| `done` | terminal | `SendActivity` + end; final output var | none |

## `support` (full coverage)
| our step | kind | MAF mapping | friction / risk |
|---|---|---|---|
| `classify` | llm + **demo rules** | Agent action, structured output = `classification.schema.json` | **F1**: the `demo:` block (regex `rules`+`default`) is OUR DemoChatClient feature, not YAML. Demo behavior moves into our `IChatClient`. |
| `classify` transitions | when `isCodeRelated` else out-of-scope | PowerFx `If` on the parsed output field | none — PowerFx conditions proven |
| `route_team` | **rule** (regex ApplicationA/B) | PowerFx `If(IsMatch(text,...), ...)` into local vars | **F2**: `onMultipleMatches: fail` + `onNoMatch: fail` → need explicit failure terminals guarded by PowerFx (both-match / no-match). Expressible, must be built by hand. |
| `assign_agent` | **map** (lookup + concat) + `assignAgent` | PowerFx `Switch(category, ...)` for role; `Concat(team,"_",role)` for id | **F3**: `assignAgent` (sets the run's active agent) is OUR concept. Must be re-created as run state our ResponseAgentProvider/orchestrator honors. |
| `locate_component` | **tool** | **MCP tool** (deterministic) via `McpToolHandler` | **F4**: this is the D1b path NOT yet validated → Phase 0 mini-spike. Also: tool currently local C# (`support.locateComponent`) → becomes an MCP server (D2). |
| `resolve` | llm | Agent action, structured output = `resolution.schema.json` | F1 (demo) again; `severityAssessment` demo logic (Scriban string ops) moves into IChatClient |
| `approval` | **human-input** (review payload) | `QuestionExecutor` / declarative ask | **F5**: our review is a titled multi-field payload; MAF's is prompt+entity. Need to map our structured review → a question the dashboard renders, and decision `approved/rejected` → branch. Adapter work. |
| `approval` transitions | approved → generate; else manual_review | PowerFx `If(decision == "approved", ...)` | none once F5 resolved |
| `generate_code` | llm | Agent action, structured output = `pull-request.schema.json` | F1 (demo) |
| `code_change_generated` | terminal success | `SendActivity` + end, output = PR | none |
| `classified_out_of_scope` | terminal success | end with message | none |
| `manual_review_required` | terminal failure | end with failure outcome | none |

## Frictions summary (what the migration must build)
- **F1 — Demo mode leaves the YAML.** Today `demo:` blocks live in workflow YAML and the engine's DemoChatClient reads them. In MAF the YAML is standard; Demo must be a behavior of OUR `IChatClient` (deterministic canned/rule-based responses), configured outside the workflow doc. **Design impact: Demo provider owns demo logic, keyed by workflow/step, not YAML.**
- **F2 — rule fail-paths.** `rule` steps with `onNoMatch`/`onMultipleMatches: fail` become explicit PowerFx-guarded failure terminals. Mechanical but must be authored.
- **F3 — `assignAgent` run state.** The "current agent of the run" concept must be preserved in our orchestrator/provider so telemetry + dashboard agent status keep working.
- **F4 — tool → MCP.** Deterministic tool steps become MCP tool calls; depends on the D1b mini-spike result.
- **F5 — human review shape.** Our rich review payload (title + labeled fields) must map onto MAF's question/approval mechanism while keeping the dashboard Approvals UX. Likely the biggest adapter piece besides the conversation-store provider (D4).

## Verdict
**Every step maps.** No blocker found. Five frictions (F1–F5), all expressible. F4 and F5 are now **VALIDATED by the Phase 0 mini-spikes** (see below). The custom `rule`/`map`/Scriban can be retired; their behavior re-expressed in PowerFx + MCP + our Demo provider.

**Gate status: FULLY PASSED ✅ — F4 and F5 validated end-to-end. Safe to proceed to engine migration (Phase 2).**

## F4 / F5 validation results (Phase 0 mini-spikes, run green)

### F4 — deterministic MCP via declarative `McpToolHandler` — PASS
- Declarative action **`InvokeMcpTool`** (`Microsoft.Agents.ObjectModel.InvokeMcpTool`): explicit, **no agent/LLM**. Carries `serverUrl`, `serverLabel`, `toolName`, `arguments`, `output.result`.
- Routed through a **custom `IMcpToolHandler`** on `DeclarativeWorkflowOptions.McpToolHandler`. Built-in `DefaultMcpToolHandler` needs a real HTTP(S) MCP endpoint → for our module-as-MCP-server model over HTTP that is actually fine; for in-process we implement the interface. **Implication for migration: the engine sets a `McpToolHandler` that reaches module MCP servers.**
- **GOTCHA (carry into Phase 2):** `InvokeMcpTool` binds the result through PowerFx; a bare numeric JSON result becomes a `Double` and throws `Unsupported element type: Double`. Return non-numeric strings from MCP tools, or wrap results in an object. `SendActivity` text needs `{expr}` interpolation, not bare `=expr`.

### F5 — human-in-the-loop via declarative action — PASS (two required substitutions)
- Use **`Question`** (NOT `RequestExternalInput`) for the gate: `RequestExternalInput` stalls after the gate (never calls `SendResultMessageAsync`) and surfaces no prompt. `Question` advances and carries the prompt.
- Use **`ConditionGroup`** (NOT `If`) for the post-resume branch: a plain `If` is not scheduled after a HITL resume (run stalls Idle); `ConditionGroup` works.
- Host drives it: `InProcessExecution.RunStreamingAsync` + `WatchStreamAsync`; on `RequestInfoEvent`, resume with `run.SendResponseAsync(request.CreateResponse(new ExternalInputResponse(new ChatMessage(User, answer))))`. Both approved and rejected branches verified.
- **Implication for migration (F5 / our `human-input`):** our rich review payload maps to a `Question` prompt; the dashboard Approvals UX drives resume via `SendResponseAsync`. The run must be resumable (checkpoint) across the gate — ties into D4 (conversation/run store provider).
