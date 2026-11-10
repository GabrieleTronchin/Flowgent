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

## DECISIONS (reviewed with owner — see status on each)

### D1. MCP wiring: agent-level vs declarative `McpToolHandler` — DECIDED
- **(a) Agent-level** (spike-proven): tools attached to the agent; the LLM decides to call them.
- **(b) Declarative `McpToolHandler`**: MCP tool calls as explicit YAML actions (deterministic, not LLM-mediated). Not yet validated.
- **DECISION:** support BOTH; **(b) is the primary path** for deterministic domain tools. Validate (b) with a mini-spike **before** the real migration, but **not now** (Phase 0, when we get there).

### D2. Fate of the existing `/_module/*` SDK (Via 3) — DECIDED
- **DECISION:** **deprecate the SDK.** New modules use MCP. Migrate `support`'s `locateComponent` to an MCP server as the reference, reach parity, then remove the SDK. MCP is the one supported extension path going forward.

### D3. `rule` / `map` deterministic steps — DECIDED
- Our engine has non-LLM deterministic steps: `rule` (branching, e.g. support team routing by text match) and `map` (key→value, e.g. IssueCategory→AgentRole). MAF declarative has `If`/`ConditionGroup`/`Foreach`/PowerFx and MCP tools instead.
- **DECISION:** simple branching → **PowerFx**; lookups/mappings → PowerFx where simple, a small deterministic **MCP tool** where the logic is non-trivial. **Hard gate:** verify every `support`/`jokes` step maps cleanly to PowerFx/MCP BEFORE deleting any of our engine code (mapping pass in Phase 1).

### D4. ResponseAgentProvider conversation store — DECIDED
- Spike used in-memory. Real engine must back conversations so events/telemetry/HITL line up.
- **DECISION:** model it as a **pluggable provider** (an abstraction the host supplies), NOT baked into the engine. Ships with a default implementation backed by our persistence/run model; can be swapped. Detailed design in Phase 2. This remains the heaviest real integration point.

### D5. Definition distribution to a stock engine image — DECIDED
- **DECISION:** **mounted volume / path is the primary mechanism** ("mount the folder and go"). Keep `ModuleCatalog` polling as a secondary option for service-style modules.

### D6. MAF version pinning / churn — LEAN, revisit later
- Declarative surfaces carry "evaluation only" flags; MAF moves fast (1.9.0..1.28.0).
- **LEAN:** owner likes "pin one stable version repo-wide + isolate MAF declarative behind a thin adapter seam", but wants to **revisit later** (not finalized). Treat as a design constraint to confirm in Phase 2.

### D7. NuGet config hygiene — DONE ✅
- The user-level NuGet.Config enables an authenticated private Azure DevOps feed (TecCom) that hangs restores for projects not covered by a closer config.
- **DONE:** added a **repo-root `nuget.config`** (clears sources, nuget.org only) covering the whole repo incl. future top-level folders. Verified: `dotnet nuget list source` from root shows only nuget.org; spike restore resolves without hanging. The existing `backend/`, `modules/`, `spike/` configs remain (redundant but harmless).

## Risks
- MAF declarative API churn (D6).
- `rule`/`map` not mapping cleanly (D3) — mitigate with the mapping pass before deleting our engine code.
- `ResponseAgentProvider` ↔ our run/event model impedance (D4) — the real integration cost; the spike's in-memory store understates it.
