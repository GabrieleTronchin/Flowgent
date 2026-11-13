# Requirements — MAF Declarative Migration + Containerization

## R1. Engine stands on MAF declarative workflows
- The engine loads workflow definitions in **MAF declarative YAML** format, read from a file/stream at runtime (`DeclarativeWorkflowBuilder.Build`).
- Our custom YAML format, semantic validator (`DefinitionValidator`) and Scriban template engine (`TemplateEngine`/`ScriptConversion`) are retired in favor of MAF's format + PowerFx.
- Rationale: remove ~2,000 lines of the highest-complexity, highest-obsolescence-risk code and inherit Microsoft's maintained declarative stack.

## R2. Our own LLM provider, no Foundry dependency
- Workflows are driven by our configurable `IChatClient` (Demo + OpenAI-compatible), bridged through a custom `ResponseAgentProvider`.
- Must run fully offline in Demo mode (no API key, no Azure credential). Verified by the spike.

## R3. Domain logic via MCP (Via 2)
- A domain app extends the engine by pointing it at an **MCP server** (its tools), configured as data — not by recompiling the engine.
- The engine image stays stock/immutable; a new app = definitions (+ optional MCP server container).

## R4. Preserve the operational surface (our real value)
- Keep and re-wire: runs, **event log with dual-write**, transactional inbox, **human-in-the-loop** gates exposed via API, **gRPC-Web streaming**, agent **telemetry**, the generic **dashboard**, the **gateway**.
- The dashboard must keep working against the migrated engine (graph, form, approvals, runs, agents, events).

## R5. Behavior parity
- The existing example modules (`support`, `jokes`) keep their end-to-end behavior after migration (same outcomes, same human-approval flow), validated against existing tests where applicable.
- `rule`/`map` deterministic steps must have a defined equivalent in the MAF model (PowerFx / actions / MCP tool). See open decision D3.

## R6. Containerization
- Engine, gateway and dashboard are each a Docker image.
- Workflow definitions are supplied to the engine at runtime (mounted volume or config), not baked into the image.
- A reference `docker-compose` brings up engine + gateway + dashboard + one example MCP tool server, working in Demo mode.

## R7. CI/CD pipelines
- Pipelines build and publish the engine/gateway/dashboard images (versioned tags), and build example MCP server images.
- Existing backend + dashboard test suites keep running in CI.

## R8. Readability cleanup
- Reduce nested/co-located types where it hurts readability (one type per file for executors/services; DTO record groups may stay grouped).
- Much of this is achieved for free by R1 (the hand-written `WorkflowCompiler` executors disappear).

## Non-goals (for now)
- Removing the existing `/_module/*` SDK (Via 3) entirely — keep it until MCP covers its cases.
- Parallel/fan-out workflows beyond what MAF declarative supports (declarative runs actions in order; use code-first only if truly needed).
- Multi-tenant / auth hardening of the engine (separate future concern).
