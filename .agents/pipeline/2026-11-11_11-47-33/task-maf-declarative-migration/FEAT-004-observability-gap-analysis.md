# FEAT-004 — Observability-contract gap analysis (MAF declarative vs. old engine)

Status: ANALYSIS ONLY. No code or scope change made. Build is green (0 warnings / 0 errors) at HEAD `45a5e1d`.

## Why this note exists

FEAT-004 is scoped to the YAML rewrites + re-enabling `SupportDefinitionTests`, but its `verification` requires
`dotnet test --project modules/support/tests` to pass, which runs `SupportParityTests` end-to-end. That suite asserts
four fine-grained **observability** behaviors that the old custom engine produced from its per-step executors and
per-step `detail:` Scriban templates. MAF declarative has **no 1:1 equivalent** for these; the FEAT-001 probes only
proved "a run reaches a terminal / Global.* is readable / HITL resumes", not these event/detail contracts.

The four behaviors asserted by `SupportParityTests` (and `SupportServiceTests`/dashboard tests):

1. Per-step `Detail` — `classify`→`"BackendBug (92%)"`, `route_team`→`"TeamA"`, `assign_agent`→`"TeamA_BackendDeveloper"`, terminals→`"[Demo] Fix OrderController.cs"`, etc.
2. Event ordering/pairing — an exact sequence where some steps emit `StepStarted`+`StepCompleted` and others (`route_team`, `assign_agent`) emit only `StepCompleted`.
3. Terminal/failure `CurrentStepId` + `Detail` — route failure keeps `CurrentStepId == "route_team"` with the verbatim reason as `Detail`; `manual_review_required` surfaces `Detail == "Fix is too risky"`/`"Rejected"`.
4. `FinalOutput` — `run.FinalOutput["title"]` is the `generate_code` step's structured JSON.

## Established facts (verified in this pass)

- **`Detail` is free-form display text.** In `RunStore.cs`, `Detail` is a `string?` on both `RunRecord` and each
  `RunTransition`/`RunEventInfo`. The dashboard (`EventLog.tsx`: `{event.detail ?? '—'}`, `RunsList.tsx`,
  `OverviewPage.tsx`) only renders it, truncated, and falls back to `—`. Dashboard tests use arbitrary strings
  (`'Event 0'`, `fc.string(...)`) and never parse detail or assert a specific per-step value. **The dashboard does not
  depend on any particular detail string.**
- **Old-engine detail was author-controlled per step.** Each step in the pre-migration YAML carried a `detail:` Scriban
  template (`classify`: `"{{ steps.classify.category }} ({{ steps.classify.confidenceScore * 100 | math.round }}%)"`,
  `route_team`: `"{{ steps.route_team.teamName }}"`, etc.). Detail was never auto-derived — it was explicitly authored.
- **The seam today emits `StepCompleted(name, node, Detail: null)` for every `Global.*` variable**, in checkpoint-file
  enumeration order, and emits `StepStarted` immediately before each `StepCompleted` (`MafRunDriver.SurfaceOutputsAsync`).
  `RunOrchestrator.OnEventAsync` forwards those straight to `RunRecorder`. There is no per-step detail derivation and no
  deterministic, selectively-paired ordering.
- **Terminal handling** (`RunOrchestrator.CompleteAsync`) sets `CurrentStepId = terminal id`, `Detail = outcome.Message`,
  `FinalOutput = outcome.Output`, where `outcome` comes from `MafRunDriver.ToCompleted` reading `WorkflowOutputEvent.Data`.
- **The dashboard DOES depend on `currentStepId` + `stepOutputs` + `assignedAgentId`** (graph highlight, step panel,
  agent badge). Those are already produced by the seam via `Global.<step>` + `Global.assignedAgentId`.

## Per-gap analysis

### Gap 1 — Per-step `Detail` (`"BackendBug (92%)"`, `"TeamA"`, …)

- **Is it intrinsic to MAF?** No. Detail is an old-engine concept (a per-step Scriban template). MAF declarative has no
  "step detail" field. But it is **author-controllable** under declarative: the YAML already writes
  `Global.<stepId>` snapshots; it can **also** write a `Global.<stepId>__detail` string via a trailing `SetVariable`
  (e.g. `=Local.classify.category & " (" & Text(Local.classify.confidenceScore*100) & "%)"`,
  `=Local.route.teamName`). The seam reads a sibling `*__detail` global and passes it as the `StepCompleted.Detail`
  instead of `null`. **One small, generic convention in `MafRunDriver` (strip `__detail`, attach to the matching
  step), no per-step C#.**
- **Classification:** **(i) cheaply + generically fixable in the seam**, driven by the YAML author. The only engine
  change is a naming-convention read; the detail *content* lives in the YAML exactly as the old `detail:` did.
- **Caveat:** `"92%"` needs PowerFx number→string with no decimals (`Text(x, "0")` or integer rounding). This is a
  PowerFx-dialect detail to confirm in the seam smoke harness, not a semantic fake. If the exact rounding can't be
  reproduced faithfully, that single assertion is a candidate for a documented test update (see "contract" below).
- **Recommendation:** adopt the `Global.<stepId>__detail` convention (seam reads it generically); author details in the
  YAML. Faithful to MAF, no over-fitting.

### Gap 2 — Event ordering / selective `StepStarted`+`StepCompleted` pairing

- **What does the test assert?** An exact list including `StepStarted:classify, StepCompleted:classify,
  StepCompleted:route_team, StepCompleted:assign_agent, StepStarted:locate_component, …` — i.e. `route_team`/
  `assign_agent` get a `StepCompleted` with **no** preceding `StepStarted`. This mirrors an **incidental artifact** of
  the old executors: LLM/tool/HITL steps logged `StepStarted` (they did async work worth showing as "in progress");
  pure rule/map steps completed synchronously and only logged `StepCompleted`.
- **Does the dashboard depend on it?** No. The event log renders events in timestamp order and shows kind+detail; it has
  no logic keyed on "route_team has no StepStarted". The ordering requirement is **over-specified by the test**, not a
  product contract.
- **Can the seam reproduce it faithfully?** Partly. The seam can emit events in **deterministic workflow order** (walk
  the descriptor graph / the YAML action order rather than checkpoint-file enumeration) — that is a genuine, generic
  improvement worth making. But reproducing the *selective* "no StepStarted for rule/map steps" rule would require the
  seam to classify each step kind and suppress `StepStarted` for `rule`/`map` — which is re-deriving an old-engine
  behavior that has no MAF meaning (every declarative action is just an action). That edge is **(ii) only reproducible
  by over-fitting**.
- **Recommendation:** make the seam emit events in **deterministic step order** (generic, faithful, worth doing). For
  the selective-pairing specifics, treat `EventLog_FollowsThePipelineSteps`'s exact sequence as **implementation-coupled**
  and update it in FEAT-005 to assert the new, still-meaningful sequence (every surfaced step emits StepStarted+
  StepCompleted in workflow order; HITL emits AwaitingInput+InputReceived; one terminal event). Document the change.

### Gap 3 — Terminal `CurrentStepId` + failure `Detail`, generically

- **Route failures keeping `CurrentStepId == "route_team"`:** In the old engine a `rule` step could itself *fail*
  (`onNoMatch.fail`/`onMultipleMatches.fail`), so the failing step id was the rule step. Under declarative, routing
  failure is modeled as a `GotoAction` to a `fail_ambiguous`/`fail_no_app` **terminal**, so the natural `CurrentStepId`
  is the terminal id, not `route_team`. Keeping `route_team` would mean the seam pretends the failure happened at the
  rule node — a **fake** unless the YAML is authored so the rule node *is* the failing step. This is really a **contract
  change**: under MAF, the failing step is the terminal. (Note the dashboard `pages.test.tsx` fixture already shows a
  `Failed` run at `route_team` with detail `"Ambiguous routing"`, but that's a fixture, not a live contract.)
- **Carrying the verbatim reason as `Detail` on the terminal:** **(i) cheaply + generically fixable.** The failure
  terminals already carry their message in the manifest/YAML; `CompleteAsync` already sets `Detail = outcome.Message`.
  Making `ToCompleted` reliably pull the terminal's message (from the reached terminal node + its `SendActivity`/message
  text, or a `Global.__terminalMessage`) is a generic seam change, not per-workflow.
- **`manual_review_required` Detail = reject reason / `"Rejected"`:** generic — the reason crosses the HITL boundary as
  `{decision, reason}` (already implemented in `MafHumanDecision.ToJson`); the terminal message just needs to surface
  `reason` (or `"Rejected"`). Faithful and generic.
- **Recommendation:** (a) generic seam fix to carry the terminal/failure **message as Detail** — do it. (b) The
  `CurrentStepId == "route_team"` expectation for routing failures is a **genuine behavior change under MAF** (failing
  step is now the terminal). Right answer: update that assertion/contract in FEAT-005 and document it, OR author the
  support YAML so routing failure is a self-failing node whose id is `route_team` (possible but more over-fit). Prefer
  the documented contract update: terminal-based failure is the honest MAF model.

### Gap 4 — `FinalOutput["title"]` from a step's structured output

- **Can the seam map it generically?** Yes. The convention is already "each step writes `Global.<stepId>` as its
  structured JSON". The terminal `code_change_generated` is defined (manifest + design §9) to carry
  `output = generate_code`. The seam can set `FinalOutput` generically to the `Global.<terminalOutputStep>` the terminal
  names, or — simplest and generic — the terminal `SendActivity` carries the final payload and `ToCompleted` already
  reads `WorkflowOutputEvent.Data`. The only work is ensuring the terminal emits the intended step's JSON object (author
  the terminal to output `Global.generate_code`) and the seam parses it to a `JsonNode`. **(i) generically fixable**, no
  per-workflow C#.
- **Recommendation:** author terminals to output the intended `Global.<step>` JSON; seam parses `WorkflowOutputEvent.Data`
  to `JsonNode` (already partly done in `ToCompleted`). Faithful, generic.

## Summary classification

| Gap | Generic+faithful seam fix | Over-fit/fake | Genuine contract change (update test, document) |
|-----|---------------------------|---------------|--------------------------------------------------|
| 1 Per-step Detail | **Yes** — `Global.<step>__detail` convention read by seam; content authored in YAML | the `92%` rounding, only if PowerFx can't format it | fallback only if rounding unfaithful |
| 2 Event ordering | **Yes** — emit in deterministic workflow order | selective "no StepStarted for rule/map" | **Yes** — update `EventLog_FollowsThePipelineSteps` exact sequence |
| 3 Terminal/failure Detail + CurrentStepId | **Yes** for carrying reason/message as Detail | faking `route_team` as current step | **Yes** — routing failure's `CurrentStepId` is now the terminal under MAF |
| 4 FinalOutput | **Yes** — terminal outputs `Global.<step>`; seam parses | — | no |

## Is `SupportParityTests` an authoritative behavior contract?

**Mixed, and that's the crux.** Two layers:

- **Business/outcome layer — authoritative, preserve verbatim.** The routing/classification/agent-mapping/severity
  outcomes, terminal *reached* (`code_change_generated`/`classified_out_of_scope`/`manual_review_required`/
  `fail_ambiguous`/`fail_no_app`), the verbatim failure messages, approved-vs-rejected behavior, `stepOutputs` content,
  `assignedAgentId`, and the HITL reason flow are the **product contract**. These MUST stay green and are reproducible
  faithfully (they're what the probes + DemoResponseProvider already target).
- **Observability-shape layer — implementation-coupled, legitimately changes under MAF.** The *exact* per-step detail
  strings, the *exact* event sequence incl. selective StepStarted suppression, and `CurrentStepId == "route_team"` on
  routing failure are coupled to the deleted executor model. Updating these per a documented behavior change is
  **correct, not weakening** — provided the business layer above is unchanged and the new observability shape is still
  coherent (deterministic order, meaningful details, terminal-based failure).

## Recommendation on scope (for the parent to decide)

- **Generic, faithful seam fixes** (Gap 1 `__detail` convention, Gap 2 deterministic ordering, Gap 3 reason/message-as-
  Detail, Gap 4 final-output mapping) → a **reopened FEAT-003-scope** item (they live in `MafRunDriver`/`RunRecorder`/
  `RunOrchestrator`).
- **Genuine contract changes** (Gap 2 exact event sequence, Gap 3 routing-failure `CurrentStepId`) → **documented test
  updates in FEAT-005**, with the business-outcome assertions preserved verbatim.
- **FEAT-004 itself** then ships: the four declarative YAMLs + manifests (+ `__detail` / terminal-output conventions),
  `DemoResponseProvider` keys (already present), and the ported `SupportDefinitionTests` (pure routing/mapping/demo
  logic), with verification limited to **build + `ModuleDefinitionAssert.IsValid`** until the seam item lands — then the
  full support suite goes green.

No faking of MAF semantics is required to hit the **business** contract; the only "faithful-or-redefine" calls are the
two observability specifics in Gaps 2 and 3, where redefining (documented) is the honest answer.
