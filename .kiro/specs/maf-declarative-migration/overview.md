# MAF Declarative Migration + Containerization — Overview & Resume Point

> **READ THIS FIRST when resuming.** This is the recap of where we were, why, and what to do next.

## 30-second recap

Flowgent today is a custom engine that: loads **our own YAML format**, validates it, runs a **Scriban** template engine, and compiles workflows onto Microsoft Agent Framework (MAF) `WorkflowBuilder` (code-first). We discovered that **MAF now ships Declarative Workflows 1.0 (GA)** — YAML-from-file, branching, human-in-the-loop, HTTP + MCP, PowerFx — which overlaps with the most complex ~2,000 lines of our code (`Definitions` validator + Scriban + `WorkflowCompiler`).

**Decision taken:** stop reinventing the declarative engine; **stand on MAF's declarative stack**, keep what is genuinely ours (module model, dashboard, inbox/event-log/telemetry, gateway, provider abstraction), and plug domain logic in over **MCP (Via 2)**. Longer term: ship **engine + gateway + dashboard as reusable Docker images**; a new app = a folder of YAML definitions (mounted) + optionally an MCP server container. The old `/_module/*` SDK (Via 3) is kept for now but de-emphasized in favor of MCP.

**Spike result:** a feasibility spike proved all of this works (see `spike/README.md`). **Via 2 is feasible.** Go ahead with the migration.

## Goal (the "why")
A reusable, open-source **engine as a product**: pull the engine/dashboard images, point them at domain definitions + MCP tool servers, and ship AI-flow apps at low cost without rebuilding the engine. Multimodal (e.g. image input) comes "for free" from MAF rather than being built by hand.

## What is DONE
- ✅ Research: MAF Declarative is GA/stable; confirmed YAML-from-file, MCP from workflow, non-Foundry agents via `ResponseAgentProvider`.
- ✅ Spike (`spike/Spike.MafDeclarative`, branch `spike/maf-declarative-mcp`): all 4 goals pass — YAML loaded from file, our fake `IChatClient` as the LLM, a real MCP tool invoked, run streamed. Build + run green. Full findings in `spike/README.md`.
- ✅ Direction chosen: Via 2 (MCP) + Docker images + pipelines.
- ✅ **Phase 2 COMPLETE** (branch `feature/maf-declarative-engine`, 6 commits, not pushed): engine migrated to MAF Declarative behind the `Flowgent.Runtime.Maf` seam; custom YAML/Scriban/validator/compiler/step-handlers deleted; `jokes`/`support`/`_template` rewritten to declarative YAML; dashboard/inbox/event-log/telemetry/HITL preserved. `dotnet build` 0/0, `dotnet test --solution` 278/278, pipeline review APPROVED. Verified e2e in Demo mode (jokes → Completed; support → through the human-approval gate → code generation). Known gap: declarative `http` step unproven (fixture bridged via `fixture.echo` MCP tool).
- ✅ **Phase 2.7 (readability) CLOSED** as already satisfied by the migration — the ugly nested code (WorkflowCompiler executors, Scriban, 463-line validator, Steps/*) was deleted, not refactored; remaining multi-type files are cohesive and left grouped. See `tasks.md` 2.7 for the rationale.

## Decisions — ALL RESOLVED ✅ (see `design.md` for each)
- D1 MCP wiring: support both; declarative `McpToolHandler` is primary; mini-spike on it in Phase 0.
- D2 `/_module/*` SDK: deprecate; MCP is the one extension path.
- D3 `rule`/`map`: PowerFx where simple, deterministic MCP tool where not; verify mapping before deleting engine code.
- D4 conversation store: a pluggable provider (default backed by our persistence); detailed design in Phase 2.
- D5 definitions: mounted volume is primary; catalog polling secondary.
- D6 MAF version: pin + thin adapter seam (lean; confirm in Phase 2).
- D7 NuGet hygiene: DONE — repo-root `nuget.config`.

## Analysis phase COMPLETE ✅
- All decisions D1–D7 resolved.
- Phase 1 step-mapping done: every `support`/`jokes` step maps to MAF (`step-mapping.md`). Gate D3 passed.
- Phase 0 mini-spikes done & GREEN: **F4** deterministic MCP via declarative `InvokeMcpTool` + custom `IMcpToolHandler`; **F5** HITL via `Question` + `ConditionGroup` with host-driven `SendResponseAsync` resume (approved + rejected branches). Spike `dotnet run` ends `A=True F4=True F5=True`.
- All 5 frictions F1–F5 now validated. **No blockers remain to start the real migration.**

## What is NOT done — next up is Phase 2 (real engine migration)
- Nothing migrated in the real engine yet — spike is throwaway/isolated.
- Remaining before coding: 0.3 (MCP-over-HTTP mini-spike, low-risk, can pair with Phase 3).
- Then Phase 2: add MAF declarative behind an adapter seam; production `ResponseAgentProvider` (+ pluggable conversation/run store, D4); re-wire `RunOrchestrator` to run MAF workflows and map events→our event-log/gRPC/HITL; load defs from mounted volume (D5); then delete the replaced `Definitions`/`WorkflowCompiler`/Scriban; port tests.
- Carry these GOTCHAS into Phase 2: MCP tools must not return bare numbers (PowerFx `Double` crash); use `Question` not `RequestExternalInput`; use `ConditionGroup` not `If` after a HITL resume; `SendActivity` uses `{expr}` interpolation.

## Where things live
- Spike code + findings: `spike/` (branch `spike/maf-declarative-mcp`, uncommitted).
- This spec: `.kiro/specs/maf-declarative-migration/` (`overview.md`, `requirements.md`, `design.md`, `tasks.md`).
- Real engine (unchanged): `backend/src/Engine/Flowgent.{Definitions,Runtime,Persistence,Contracts}`, services, modules.

## Next action when resuming
1. Skim `spike/README.md` Findings (5 min).
2. Resolve the **Open decisions** in `design.md` (that's the real blocker — mostly needs your calls).
3. Then execute `tasks.md` phase by phase. Phase 0 (preliminary) first.

## Branches / git state
- `spike/maf-declarative-mcp` — current branch, holds the spike (uncommitted working-tree changes).
- `feature/llm-flows-platform` — the Flowgent platform branch (already on origin).
- `master` — old code; its stray working-tree edits were discarded on purpose.
