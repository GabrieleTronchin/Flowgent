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
`dotnet run` runs four demos (A, F4, F5, Gate-0) plus the ASP.NET-Core-leak check, and ends with `[summary] A=True F4=True F5=True Gate0Http=True NoAspNetCoreLeak=True` / `=== Spike complete (all demos passed) ===`. See "Gate-0 (FEAT-001): MCP-over-HTTP" below.

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

## Gate-0 (FEAT-001): MCP-over-HTTP — **PASS** ✅

Hard make-or-break gate for the containerize-and-mcp task: prove a declarative
`InvokeMcpTool` action can reach a REAL MCP server over HTTP, fully offline and
deterministically, before committing to any later phase. Added as a fourth demo
(`GATE-0`) plus the ASP.NET-Core-leak check. Reproduce with
`cd spike/Spike.MafDeclarative && dotnet run`; the summary now ends with
`Gate0Http=True NoAspNetCoreLeak=True` / `=== Spike complete (all demos passed) ===`.

What it exercises, fully on loopback (no external network):
- A real in-process HTTP MCP server (`ModelContextProtocol.AspNetCore`):
  `builder.Services.AddMcpServer().WithHttpTransport().WithTools<EchoUpperTool>()` +
  `app.MapMcp()`, Kestrel bound to `http://127.0.0.1:0`, the actual port read back via
  `IServerAddressesFeature` (`McpHttpEchoServer.cs`).
- One deterministic tool `echoUpper(text)` returning the uppercased input as a
  NON-numeric string (finding F4).
- `workflow.mcphttp.yaml`: a single `InvokeMcpTool` action; its `serverUrl` placeholder
  is replaced at runtime with the real bound URL and the workflow is built from the
  resulting text via the `DeclarativeWorkflowBuilder.Build<string>(TextReader, ...)`
  overload.
- The custom `HttpMcpToolHandler` (`HttpMcpToolHandler.cs`), set on
  `DeclarativeWorkflowOptions.McpToolHandler`, lazily connecting one `McpClient` per
  `serverUrl`.

### The four Gate-0 outcomes

1. **InvokeMcpTool result crosses PowerFx with NO "Unsupported element type: Double".**
   PASS. `echoUpper("gate-zero mcp over http")` returns the non-numeric string
   `GATE-ZERO MCP OVER HTTP`; it is read back with `First(Local.mcpResult).Value` and
   flows into the final activity `MCP echoUpper(gate-zero mcp over http) =
   GATE-ZERO MCP OVER HTTP`. No `Double` error. (This confirms finding F4 holds over the
   HTTP transport exactly as over the in-memory pipe.)

2. **Adopted handler: the custom `HttpMcpToolHandler`.** There is NO built-in/default
   handler to adopt in MAF 1.27.0. The "primary attempt" (`DefaultMcpToolHandler`) could
   not even be constructed: **no `DefaultMcpToolHandler` type exists in any
   `Microsoft.Agents.AI*` 1.27.0 assembly** — a scan of those assemblies finds only the
   `IMcpToolHandler` interface and the `McpToolHandler` options property, no concrete
   handler. So a custom `IMcpToolHandler` is mandatory, and the adopted handler is the
   custom `HttpMcpToolHandler` (HTTP analogue of `SpikeMcpToolHandler`: connect an
   `McpClient` to `serverUrl`, `CallToolAsync`, map `TextContentBlock` → `TextContent`
   into `McpServerToolResultContent.Outputs`).
   > Correction to earlier finding F4, which speculated a built-in `DefaultMcpToolHandler`
   > connecting over HTTP: that type does not exist in 1.27.0. Later phases must not
   > assume a stock HTTP handler; they inherit the spike's custom handler shape.

3. **External (caller-owned) `HttpClient` + `HttpClientTransport` constructor works
   end-to-end with `McpClient.CreateAsync`.** PASS. `HttpMcpToolHandler` builds
   `new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(serverUrl),
   Name = serverLabel }, httpClient, loggerFactory: null, ownsHttpClient: false)` over a
   caller-owned `HttpClient` and connects with
   `McpClient.CreateAsync(transport, cancellationToken: ct)`; the tool call succeeds over
   real HTTP. This is the injectable-HttpClient constructor the later engine work (and its
   resilience/service-discovery handlers) depends on.

4. **ASP.NET-Core-leak result: NO LEAK.** `AspNetCoreLeakCheck.Run()` reflects over the
   assembly that defines the client type `McpClient` — `ModelContextProtocol.Core`
   (1.4.2.0) — and asserts via `Assembly.GetReferencedAssemblies()` that no referenced
   assembly name starts with `Microsoft.AspNetCore`. Result: of 30 referenced assemblies,
   **zero** are `Microsoft.AspNetCore.*`. So a plain classlib referencing only the client
   package (`ModelContextProtocol`) does NOT drag in ASP.NET Core. (The AspNetCore server
   support lives in the separate `ModelContextProtocol.AspNetCore` package, which this
   spike's Exe references only for the HTTP server — it is NOT pulled in transitively by
   the client type.)

### Gate-0 implications for later FEATs

- **FEAT-002 (package placement):** the client package `ModelContextProtocol` is
  ASP.NET-Core-free, so a client-only consumer (e.g. an engine-side handler or a parity
  test) can reference `ModelContextProtocol` alone; only a project that actually hosts an
  MCP server needs `ModelContextProtocol.AspNetCore`. Both are now central
  `<PackageVersion>` entries (ItemGroup `Label="MCP"`) in `backend/Directory.Packages.props`.
- **FEAT-003 (in-process parity-test feasibility):** feasible. A real HTTP MCP server can
  be stood up in-process on a loopback port and driven end-to-end offline (Kestrel +
  `MapMcp`, port 0, read-back address), and the client half references no ASP.NET Core — so
  a parity test can host the server and connect a client without a leak into a client-only
  assembly.
- **Adopted handler is custom, not stock:** later code must carry its own `IMcpToolHandler`
  (the `HttpMcpToolHandler` shape), because MAF 1.27.0 provides none.

### stdout evidence (Gate-0)

```
[MCP HTTP server] started, bound to http://127.0.0.1:52615 (echoUpper tool, offline loopback)
[HttpMcpToolHandler] InvokeToolAsync -> serverUrl='http://127.0.0.1:52615' label='spike_echo_upper_http' tool='echoUpper' args=[text=gate-zero mcp over http] (NO LLM in this path, over HTTP)
[HttpMcpToolHandler] connecting McpClient to 'http://127.0.0.1:52615' (HttpClientTransport, external HttpClient)
[MCP HTTP server] echoUpper invoked: text="gate-zero mcp over http" -> "GATE-ZERO MCP OVER HTTP"
[HttpMcpToolHandler] tool returned 1 content block(s): "GATE-ZERO MCP OVER HTTP"
[MessageActivityEvent] MCP echoUpper(gate-zero mcp over http) = GATE-ZERO MCP OVER HTTP
[Gate0 final] MCP echoUpper(gate-zero mcp over http) = GATE-ZERO MCP OVER HTTP
[leak-check] client assembly: ModelContextProtocol.Core (1.4.2.0)
[leak-check] total referenced assemblies: 30
[leak-check] RESULT: NO LEAK — the client assembly references no Microsoft.AspNetCore assembly.
[summary] A=True F4=True F5=True Gate0Http=True NoAspNetCoreLeak=True
```

## Migration implications (F4 + F5)

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
- `workflow.mcphttp.yaml` — Gate-0: declarative `InvokeMcpTool` over HTTP (serverUrl placeholder substituted at runtime).
- `McpHttpEchoServer.cs` — Gate-0: real in-process HTTP MCP server (`ModelContextProtocol.AspNetCore`, `MapMcp`, port 0) exposing `echoUpper`.
- `HttpMcpToolHandler.cs` — Gate-0: custom `IMcpToolHandler` connecting a real `McpClient` over `HttpClientTransport` with an external `HttpClient`.
- `AspNetCoreLeakCheck.cs` — Gate-0 check 4: reflects over the MCP client assembly and asserts no `Microsoft.AspNetCore.*` reference.
- `FakeChatClient.cs` — deterministic offline LLM that emits a tool call then a final answer.
- `McpWordCountServer.cs` — real MCP server+client over in-memory pipes (Goal 3 + F4 `CallToolAsync`).
- `nuget.config` — nuget.org only (see Finding 1).
