# Tasks — MAF Declarative Migration + Containerization

Execute in order. Each phase ends with a green build + relevant tests. Do NOT delete our engine code until Phase 1 confirms the mapping (D3).

## Phase 0 — Preliminary (close spike gaps + decisions)
These are the spike follow-ups and must-answer items before real migration.
- [x] 0.1 Resolve open decisions D1–D7 in `design.md` — DONE. All resolved: D1 both/`McpToolHandler` primary, D2 deprecate SDK, D3 PowerFx+MCP with mapping gate, D4 pluggable provider, D5 mounted volume primary, D6 pin+adapter (confirm Phase 2), D7 done.
- [ ] 0.2 **Mini-spike: declarative `McpToolHandler`** (D1b). Prove an explicit MCP tool *action* in YAML (deterministic, not LLM-mediated), not just agent-level tools. Extend `spike/` or a sibling. Record result in `spike/README.md`.
- [ ] 0.3 **Mini-spike: MCP over HTTP** (not in-memory pipes). Prove `McpClient` against a real Streamable-HTTP MCP server in its own container/process — this is the production transport for Via 2. Record result.
- [x] 0.4 **NuGet hygiene** (D7): DONE — added repo-root `nuget.config` (nuget.org only) covering the whole repo and any future top-level folders. Verified restore no longer hangs on the private feed.
- [ ] 0.5 Decide definition-distribution mechanism (D5) for the stock engine image.

## Phase 1 — Format mapping (no deletions yet)
- [x] 1.3 **Mapping pass DONE** — written step-by-step mapping of `support` (all step kinds) and `jokes` in `step-mapping.md`. **Gate PASSED: every step maps**, no blocker. Found 5 frictions F1–F5 (demo leaves YAML; rule fail-paths; assignAgent run state; tool→MCP; HITL review shape). F4 (McpToolHandler) and F5 (HITL shape) still need mini-spike validation before deleting engine code.
- [ ] 1.1 Rewrite `jokes` as MAF declarative YAML and run through a MAF path to confirm parity (do together with the Phase 0 mini-spikes).
- [ ] 1.2 Rewrite `support` likewise once F4/F5 mini-spikes pass; use the mapping table as the checklist.

## Phase 2 — Engine core migration
- [ ] 2.1 Add `Microsoft.Agents.AI.Workflows.Declarative` (pinned) to the engine; add a thin adapter seam around MAF declarative types (D6).
- [ ] 2.2 Implement the production `ResponseAgentProvider`: agent resolution from `ModuleCatalog`/config, bridge to our `IChatClient` (Demo/OpenAI), telemetry hook, conversation store backed by persistence (D4).
- [ ] 2.3 Re-wire `RunOrchestrator` to build the workflow via `DeclarativeWorkflowBuilder` and run it, mapping MAF events → our run transitions / event-log (dual-write) / gRPC stream / HITL.
- [ ] 2.4 Load definitions per D5 (mounted path and/or catalog).
- [ ] 2.5 Delete the replaced code: `Flowgent.Definitions` custom loader/validator/Scriban, `WorkflowCompiler` + step executors + `Steps/*`. Fix references.
- [ ] 2.6 Update/port tests (engine, integration, architecture). Keep behavior-parity tests for `support`/`jokes`.
- [ ] 2.7 Readability cleanup (R8) on remaining code: split nested executor/service types; keep DTO groups.

## Phase 3 — MCP module path
- [ ] 3.1 Migrate `support`'s `locateComponent` tool to a standalone MCP server (reference implementation).
- [ ] 3.2 Document "how to add a module via MCP" (replaces/augments the `/_module` SDK docs). Decide SDK deprecation timing (D2).

## Phase 4 — Containerization
- [ ] 4.1 Dockerfile for engine, gateway, dashboard (multi-stage, .NET 10 / Node).
- [ ] 4.2 Reference `docker-compose.yml`: engine + gateway + dashboard + one example MCP server, Demo mode, definitions mounted as a volume. Must run end-to-end offline.
- [ ] 4.3 Verify a run end-to-end through the composed stack (reuse the Playwright flow used earlier).

## Phase 5 — CI/CD
- [ ] 5.1 Pipeline: build + publish engine/gateway/dashboard images with versioned tags (e.g. GHCR).
- [ ] 5.2 Pipeline: build example MCP server image(s).
- [ ] 5.3 Keep existing backend + dashboard test jobs; add image build to CI.
- [ ] 5.4 Update README (badges/repo slug already pending the GitHub rename) and docs for the image-based usage model.

## Housekeeping (carried over, independent)
- [ ] H1 Fix dashboard tooltip text `LlmProvider:Provider` → `Flowgent:Llm:Provider`.
- [ ] H2 After GitHub repo rename: update `origin` remote + README CI badge URLs; push local commits on `feature/llm-flows-platform`.
- [ ] H3 Decide spike disposition: keep `spike/` as reference or delete before merging the migration.
