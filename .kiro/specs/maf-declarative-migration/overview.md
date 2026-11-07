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

## What is NOT done / open
- Nothing migrated in the real engine yet — the spike is throwaway, isolated, not committed.
- Several design decisions are still open (see `design.md` → "Open decisions").

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
