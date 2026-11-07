# Design — MAF Declarative Migration + Containerization

## Target architecture

```
Dashboard (image) ─┐
Clients ───────────┤
                   ▼
              Gateway (image)         single entry point (YARP)
                   ▼
              Engine (image, STOCK)   MAF DeclarativeWorkflowBuilder
                   │                  - loads YAML defs from a mounted volume
                   │                  - ResponseAgentProvider -> our IChatClient (Demo/OpenAI)
                   │                  - runs, event-log (dual-write), inbox, HITL, telemetry, gRPC stream
             MCP (standard)
        ┌──────────┼──────────┐
   MCP app A   MCP app B   3rd-party MCP   ← domain logic, separate containers
```

A new app = a folder of MAF-declarative YAML definitions (mounted) + optionally one MCP server container. Engine/gateway/dashboard images never rebuild per app.

## What the spike proved (evidence base)
- `DeclarativeWorkflowBuilder.Build<string>(file, options, inputTransform)` loads YAML from disk and returns a standard `Workflow`.
- A custom `ResponseAgentProvider` resolves the agent by name to a `ChatClientAgent` on our `IChatClient` — no Foundry.
- MCP tools (from the official `ModelContextProtocol` SDK) are invoked during the agent turn when handed in as `AIFunction`s via the provider's `Functions` + `ChatOptions.Tools`.
- PowerFx expressions in YAML work.
- All offline/deterministic. See `spike/README.md`.

## Component-level plan

### Keep (our value, re-wired)
- `Flowgent.Runtime`: `RunOrchestrator` (adapt to run a MAF `Workflow` from `DeclarativeWorkflowBuilder`), `RunRecorder`, `RunStore`, `RunUpdateChannel`, `ModuleCatalog`, `InboxProcessor`, `LlmTelemetry`, `ChatClientSetup` (our provider).
- `Flowgent.Persistence`, `Flowgent.Contracts` (API/gRPC), `Flowgent.Engine.Service`, `Flowgent.Gateway`, dashboard.

### Replace
- `Flowgent.Definitions` (custom loader/model/validator/Scriban/YAML reader) → MAF declarative YAML + `DeclarativeWorkflowOptions` + PowerFx.
- `Flowgent.Runtime/Execution/WorkflowCompiler.cs` + `StepExecutor`/`TerminalStepExecutor` + `Flowgent.Runtime/Steps/*` → gone; MAF builds the graph.

### New
- `ResponseAgentProvider` implementation bridging MAF → our run model, agent resolution (from `ModuleCatalog`/config), telemetry hook, and conversation store backed by our persistence (not just in-memory as in the spike).
- MCP integration point for modules (replace/augment the `/_module/tools` SDK path).
- Dockerfiles (engine, gateway, dashboard) + reference `docker-compose` + CI image-publish pipelines.

## OPEN DECISIONS (resolve these first when resuming)

### D1. MCP wiring: agent-level vs declarative `McpToolHandler`
The spike wired MCP at the **agent level** (tools handed to the `ChatClientAgent` via `Functions`), NOT via `DeclarativeWorkflowOptions.McpToolHandler`. Two options:
- **(a) Agent-level** (spike-proven): tools attached to the agent; the LLM decides to call them. Simple, works, but tool use is LLM-driven, not an explicit workflow step.
- **(b) Declarative `McpToolHandler`**: MCP tool calls as explicit YAML actions (deterministic, not LLM-mediated). Not yet validated.
- **Recommendation:** validate (b) in a short follow-up spike; likely we want BOTH (explicit MCP steps for deterministic calls, agent-level for LLM-driven tool use). **Needs your call + a mini-spike.**

### D2. Fate of the existing `/_module/*` SDK (Via 3)
- Keep as a legacy path, or replace module tools with MCP servers?
- **Recommendation:** new modules use MCP; migrate `support`'s `locateComponent` tool to an MCP server as the reference; keep the SDK until parity, then deprecate. **Needs your call on timing.**

### D3. `rule` / `map` deterministic steps
- Our engine has non-LLM deterministic steps (`rule`, `map`). MAF declarative has `If`/`ConditionGroup`/`Foreach`/PowerFx and `InvokeFunctionTool`/MCP.
- **Recommendation:** express simple branching as PowerFx conditions/actions; express lookups/mappings as PowerFx or a tiny deterministic MCP tool. Confirm each `support`/`jokes` step maps cleanly before committing. **Needs a mapping pass (task in Phase 1).**

### D4. ResponseAgentProvider conversation store
- Spike used in-memory. Real engine should back conversations with our persistence/run model so events/telemetry/HITL line up.
- **Recommendation:** design this against the existing `RunStore`/event-log. **Design task in Phase 2.**

### D5. Definition distribution to a stock engine image
- How does the engine image receive the YAML defs: mounted volume, config path, or pulled from a module endpoint (current `ModuleCatalog` polling)?
- **Recommendation:** support a mounted volume/path as primary for the "copy-config-and-go" model; keep catalog polling as an option. **Needs your call on the primary mechanism.**

### D6. MAF version pinning / churn
- Declarative surfaces carry "evaluation only" flags; MAF moves fast (1.9.0..1.28.0). Pin a stable version and isolate MAF types behind our own seam so upgrades are contained.
- **Recommendation:** pin one version repo-wide; wrap MAF declarative usage in a thin adapter. **Design constraint, not a blocker.**

### D7. NuGet config hygiene
- The user-level NuGet.Config has an authenticated private Azure DevOps feed that hangs restores for projects outside `backend/` (bit us in the spike).
- **Recommendation:** every project gets a local `nuget.config` (nuget.org only), or disable that user feed. **Low effort, do in Phase 0.**

## Risks
- MAF declarative API churn (D6).
- `rule`/`map` not mapping cleanly (D3) — mitigate with the mapping pass before deleting our engine code.
- `ResponseAgentProvider` ↔ our run/event model impedance (D4) — the real integration cost; the spike's in-memory store understates it.
