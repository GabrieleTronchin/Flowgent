# Spike: MAF Declarative Workflows + MCP (Via 2)

Isolated feasibility spike. **Does not touch the existing engine.** Throwaway code to validate four things before committing to the migration:

1. Load a declarative workflow from a **YAML file at runtime** via `DeclarativeWorkflowBuilder.Build(path/TextReader, options)`.
2. Drive a **non-Foundry agent** (our own `IChatClient` — a fake "Demo" client, no API key) through a custom `ResponseAgentProvider`.
3. Invoke a **tool from an MCP server** inside the declarative workflow (the Via 2 extensibility model).
4. Observe the run (streaming / final output).

## Why
Decide whether the Flowgent engine should drop its custom YAML loader / validator / Scriban template engine and stand on Microsoft Agent Framework's declarative stack, with domain logic plugged in over MCP.

## Status — ALL FOUR GOALS PASSED ✅
- [x] 1. YAML-from-file load — `DeclarativeWorkflowBuilder.Build<string>(workflowFile, options, inputTransform)` reads `workflow.yaml` from disk at runtime.
- [x] 2. non-Foundry agent via custom `ResponseAgentProvider` — `SpikeAgentProvider` resolves the agent name to a `ChatClientAgent` built on our `FakeChatClient`. Fully offline, no API key, no Azure.
- [x] 3. MCP tool invoked during the workflow — a real MCP server (official `ModelContextProtocol` SDK) exposes a `wordCount` tool; the agent calls it during its turn and the result flows back into the answer.
- [x] 4. run observed end to end — events streamed to stdout, final output captured.

**Verdict: Via 2 is FEASIBLE.** A declarative MAF workflow, loaded from a file at runtime, driven by our own `IChatClient`, can invoke MCP tools and run fully offline. Build + run are green.

Follow-up frictions **F4** (declarative `McpToolHandler`) and **F5** (declarative HITL) are also validated — see "Phase 0 mini-spikes (F4, F5)" below.

## How to reproduce
```
cd spike/Spike.MafDeclarative
dotnet run
```
`dotnet run` runs three demos (A, F4, F5) and ends with `[summary] A=True F4=True F5=True` / `=== Spike complete (all demos passed) ===`.

## Package versions used (nuget.org)
- `Microsoft.Agents.AI` 1.27.0
- `Microsoft.Agents.AI.Workflows` 1.27.0
- `Microsoft.Agents.AI.Workflows.Declarative` 1.27.0  (versions exist up to 1.28.0; 1.9.0+ are stable, earlier are rc/preview)
- `Microsoft.Extensions.AI` 10.10.0
- `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` 1.4.2

## Findings (important nuances for the migration)

1. **NuGet gotcha (cost us the first run).** The spike lives outside `backend/`, so it inherited the *user-level* `NuGet.Config`, which has an authenticated private Azure DevOps feed (TecCom) enabled. `dotnet restore` hung waiting for credentials instead of erroring. Fix: `spike/nuget.config` that `<clear/>`s sources and uses only nuget.org. **Lesson for the real work: every project outside `backend/` needs its own `nuget.config`, or we disable that user feed.**

2. **Agent invocation action is `InvokeAzureAgent` — but it is NOT Azure-bound.** The declarative schema ships only this one agent-invocation action. At runtime the agent is resolved purely by name through our `ResponseAgentProvider`; no Foundry/Azure credential is touched. Confirmed working with a fake local LLM. (Naming is misleading but harmless.)

3. **Two MCP paths exist; both now validated.** The original demo (A) is "agent-level": MCP tools (`McpClientTool`, which derive from `AIFunction`) are handed to the agent via `ResponseAgentProvider.Functions` + `ChatOptions.Tools`, and `AsAIAgent` adds the function-invoking middleware — the LLM decides when to call. The second path, the declarative `DeclarativeWorkflowOptions.McpToolHandler` driven by the `InvokeMcpTool` action, is validated in **F4 below** (deterministic, no LLM). Both work offline.

4. **MCP transport: in-memory pipes, not HTTP.** To keep the spike offline/deterministic the MCP server + client are wired over `System.IO.Pipelines` with `StreamServerTransport`/`StreamClientTransport` (no child process, no port). In production a domain module would instead be a separate MCP server over HTTP (Streamable HTTP) — the client code (`McpClient.CreateAsync` + `ListToolsAsync`) is the same shape, only the transport changes.

5. **PowerFx expressions work** in the YAML (`=System.ConversationId`, `=UserMessage(...)`, `{MessageText(Local.AgentAnswer)}`). This replaces our Scriban templates.

6. **`ResponseAgentProvider` is non-trivial to implement.** Besides `InvokeAgentAsync` we had to implement an in-memory conversation/message store (`CreateConversationAsync`, `CreateMessageAsync`, `GetMessageAsync`, `GetMessagesAsync`). Small here, but in the real engine this is the bridge to our run/agent model and needs proper design.

7. **"Evaluation only" surfaces.** Suppressed `MEAI001`/`OPENAI001` experimental warnings to build. Some declarative chat-client factory types are marked "subject to change". The core `DeclarativeWorkflowBuilder` + `ResponseAgentProvider` path we used was stable enough; watch for churn across MAF versions.

## Phase 0 mini-spikes (F4, F5)

Two follow-up frictions validated on the same offline/deterministic harness (no API key, no
Azure/Foundry, no network). `dotnet run` now runs three demos in sequence: (A) the original
agent-level demo above, (F4) the declarative MCP-handler path, (F5) the HITL gate. All three
pass; representative stdout is quoted below. Reproduce with `cd spike/Spike.MafDeclarative && dotnet run`.

API facts were confirmed by reflecting over the 1.27.0 assemblies and by decompiling the
executors with `ilspycmd` (the key method bodies are cited below). Content was rephrased for
compliance with licensing restrictions.

### F4 — deterministic MCP tool via the DECLARATIVE `McpToolHandler` — **PASS** ✅

- **Declarative action kind used:** `InvokeMcpTool` (`BotElementKind.InvokeMcpTool`,
  `Microsoft.Agents.ObjectModel.InvokeMcpTool`). This is an explicit, deterministic action —
  **no agent and no LLM** are in this path. In the YAML it carries `serverUrl`, `serverLabel`,
  `toolName`, `arguments` and `output.result`.
- **MCP handler approach:** a **custom `IMcpToolHandler`** (`SpikeMcpToolHandler`), set on
  `DeclarativeWorkflowOptions.McpToolHandler`. We did **not** use the built-in
  `DefaultMcpToolHandler` because it connects to a real HTTP(S) MCP endpoint
  (`httpClientProvider` keyed by `serverUrl`, Streamable-HTTP session). Our server is wired over
  in-memory pipes, so the handler routes `InvokeToolAsync` straight to the already-connected
  in-process `McpClient` (`McpClient.CallToolAsync(toolName, arguments)`). The YAML `serverUrl`
  (`inproc://spike-wordcount`) is used only as a label; no HTTP is made.
- **Key types/methods:** `IMcpToolHandler.InvokeToolAsync(serverUrl, serverLabel, toolName,
  arguments, headers, connectionName, ct)` → `Task<McpServerToolResultContent>`;
  `McpServerToolResultContent(callId){ Outputs = [...AIContent] }`;
  `DeclarativeWorkflowOptions.McpToolHandler`. The executor is
  `InvokeMcpToolExecutor` — with `requireApproval: false` it calls
  `mcpToolHandler.InvokeToolAsync(...)` directly and binds the result into `output.result`.
- **"Evaluation only" / instability found:** the executor binds the tool result through PowerFx.
  `InvokeMcpToolExecutor.AssignResultAsync` reads each `TextContent`, runs `JsonDocument.Parse`
  on the text, and if the text is a bare JSON number it becomes a `System.Double`;
  `FormulaValueExtensions.ToTable` then throws `DeclarativeModelException: Unsupported element
  type: Double`. **A bare numeric tool result crashes the action.** Workaround: return a
  non-JSON string (our tool returns `"5 words"`, kept verbatim). The result lands as a
  single-column PowerFx table (column `Value`), so the final activity reads it with
  `First(Local.mcpResult).Value`. Also note `SendActivity` text must use `{expr}` interpolation,
  not a bare `=expr` (a bare `=` is emitted literally).
- **stdout evidence:**
  ```
  [SpikeMcpToolHandler] InvokeToolAsync -> serverUrl='inproc://spike-wordcount' label='spike_wordcount' tool='wordCount' args=[text=one two three four five] (NO LLM in this path)
  [MCP server] wordCount invoked: text="one two three four five" -> 5
  [SpikeMcpToolHandler] tool returned 1 content block(s): "5 words"
  [MessageActivityEvent] MCP wordCount(one two three four five) = 5 words
  [F4 final] MCP wordCount(one two three four five) = 5 words
  ```

### F5 — human-in-the-loop via the declarative HITL action — **PASS** ✅ (with two required substitutions)

- **Declarative action kind used:** `Question` (`BotElementKind.Question`) for the gate, plus
  `ConditionGroup` for the branch. The host answers the surfaced request; the stored value is
  branched on, mirroring the support approval gate (approved → one activity, rejected → another).
- **Two substitutions were required** (both are negative findings about 1.27.0, each confirmed by
  decompiling the executor):
  - **`Question`, not `RequestExternalInput`.** Both surface an `ExternalInputRequest` and both
    accept an `ExternalInputResponse`, but `RequestExternalInputExecutor.CaptureResponseAsync`
    only raises a completion event — it does **not** call `SendResultMessageAsync`, so the run
    **stalls after the gate and the next action never runs**. `QuestionExecutor.CaptureResponseAsync`
    does call `SendResultMessageAsync`, so the workflow advances. (`RequestExternalInput` also
    builds an empty `AgentResponse`, so its prompt text is never surfaced; `Question` carries the
    prompt — stdout shows the real prompt text.)
  - **`ConditionGroup`, not `If`.** After a HITL (Question) resume, plain sequential actions chain
    fine (verified with two back-to-back `SendActivity` actions), but a declarative `If` action is
    **not scheduled** — the run stalls `Idle`. Swapping `If` for `ConditionGroup` (same
    semantics: `conditions[].condition/actions` + `elseActions`) makes the branch run.
- **Host driving:** `InProcessExecution.RunStreamingAsync` + `WatchStreamAsync`; on
  `RequestInfoEvent` print `request.Data` (an `ExternalInputRequest`, whose `AgentResponse.Text`
  is the prompt), then resume with
  `run.SendResponseAsync(request.CreateResponse(new ExternalInputResponse(new ChatMessage(User, answer))))`.
  The scripted answer is extracted by the `StringPrebuiltEntity` into `Local.approval`. Both an
  approved and a rejected run are driven to show both branches.
- **Key types/methods:** `Question` / `RequestExternalInput` (ObjectModel);
  `Events.ExternalInputRequest { AgentResponse }`, `Events.ExternalInputResponse(ChatMessage)`;
  `RequestInfoEvent.Request` (`ExternalRequest`), `ExternalRequest.CreateResponse(data)`,
  `StreamingRun.SendResponseAsync(ExternalResponse)`, `StreamingRun.GetStatusAsync()` (`RunStatus`).
- **stdout evidence:**
  ```
  [RequestInfoEvent] external input requested; prompt: "Approve this request? Reply 'approved' or 'rejected'."
  [host] supplying scripted response: "approved"
  [MessageActivityEvent] APPROVED branch taken. Decision = approved. Proceeding with the request.
  [result] requestSurfaced=True responded=True capturedValueReachedNextAction=True branchTaken=True finalStatus=Idle
  ...
  [host] supplying scripted response: "rejected"
  [MessageActivityEvent] REJECTED branch taken. Decision = rejected. Request will not proceed.
  ```

### Migration implications (F4 + F5)

- The declarative MCP path works offline with a custom `IMcpToolHandler`, so a domain module can
  expose deterministic tools without any agent/LLM in the loop. But the `InvokeMcpTool` result
  binding is brittle (numeric-looking results crash); tool outputs should be returned as
  non-numeric strings or structured records, and this needs a hard look before we rely on it.
- HITL is viable but only via `Question` + `ConditionGroup`; `RequestExternalInput` and `If` are
  currently unusable after a resume in 1.27.0. We should pin these shapes (and re-test on each MAF
  bump), or raise the two executor bugs upstream.

## Files
- `Program.cs` — orchestrates demos A / F4 / F5 and prints each run.
- `workflow.yaml` — demo A: the original declarative workflow (agent-level MCP), loaded from disk.
- `workflow.mcp.yaml` — F4: declarative `InvokeMcpTool` workflow (no agent/LLM).
- `workflow.hitl.question.yaml` — F5: `Question` gate + `ConditionGroup` branch (the working HITL path).
- `workflow.hitl.yaml` — F5 negative artifact: the `RequestExternalInput` + `If` variant that stalls (kept for reference, not run).
- `SpikeAgentProvider.cs` — custom `ResponseAgentProvider` over our `IChatClient` (Goal 2 + agent-level MCP wiring).
- `SpikeMcpToolHandler.cs` — F4: custom `IMcpToolHandler` routing to the in-process MCP client.
- `FakeChatClient.cs` — deterministic offline LLM that emits a tool call then a final answer.
- `McpWordCountServer.cs` — real MCP server+client over in-memory pipes (Goal 3 + F4 `CallToolAsync`).
- `nuget.config` — nuget.org only (see Finding 1).
