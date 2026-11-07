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

## How to reproduce
```
cd spike/Spike.MafDeclarative
dotnet run
```
Expected tail: `[final output] [fake-llm] The MCP wordCount tool reported 7 word(s) in your message.`

## Package versions used (nuget.org)
- `Microsoft.Agents.AI` 1.27.0
- `Microsoft.Agents.AI.Workflows` 1.27.0
- `Microsoft.Agents.AI.Workflows.Declarative` 1.27.0  (versions exist up to 1.28.0; 1.9.0+ are stable, earlier are rc/preview)
- `Microsoft.Extensions.AI` 10.10.0
- `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` 1.4.2

## Findings (important nuances for the migration)

1. **NuGet gotcha (cost us the first run).** The spike lives outside `backend/`, so it inherited the *user-level* `NuGet.Config`, which has an authenticated private Azure DevOps feed (TecCom) enabled. `dotnet restore` hung waiting for credentials instead of erroring. Fix: `spike/nuget.config` that `<clear/>`s sources and uses only nuget.org. **Lesson for the real work: every project outside `backend/` needs its own `nuget.config`, or we disable that user feed.**

2. **Agent invocation action is `InvokeAzureAgent` — but it is NOT Azure-bound.** The declarative schema ships only this one agent-invocation action. At runtime the agent is resolved purely by name through our `ResponseAgentProvider`; no Foundry/Azure credential is touched. Confirmed working with a fake local LLM. (Naming is misleading but harmless.)

3. **MCP wiring is "agent-level", NOT the declarative `McpToolHandler`.** We did NOT use `DeclarativeWorkflowOptions.McpToolHandler`. Instead the MCP tools (`McpClientTool`, which derive from `AIFunction`) are handed to the agent via `ResponseAgentProvider.Functions` + `ChatOptions.Tools`, and `AsAIAgent` adds the function-invoking middleware that executes the call. This is the pattern that worked cleanly. The declarative-level `McpToolHandler` path was NOT validated and remains an open question (see spec).

4. **MCP transport: in-memory pipes, not HTTP.** To keep the spike offline/deterministic the MCP server + client are wired over `System.IO.Pipelines` with `StreamServerTransport`/`StreamClientTransport` (no child process, no port). In production a domain module would instead be a separate MCP server over HTTP (Streamable HTTP) — the client code (`McpClient.CreateAsync` + `ListToolsAsync`) is the same shape, only the transport changes.

5. **PowerFx expressions work** in the YAML (`=System.ConversationId`, `=UserMessage(...)`, `{MessageText(Local.AgentAnswer)}`). This replaces our Scriban templates.

6. **`ResponseAgentProvider` is non-trivial to implement.** Besides `InvokeAgentAsync` we had to implement an in-memory conversation/message store (`CreateConversationAsync`, `CreateMessageAsync`, `GetMessageAsync`, `GetMessagesAsync`). Small here, but in the real engine this is the bridge to our run/agent model and needs proper design.

7. **"Evaluation only" surfaces.** Suppressed `MEAI001`/`OPENAI001` experimental warnings to build. Some declarative chat-client factory types are marked "subject to change". The core `DeclarativeWorkflowBuilder` + `ResponseAgentProvider` path we used was stable enough; watch for churn across MAF versions.

## Files
- `Program.cs` — orchestrates the four goals and prints the run.
- `workflow.yaml` — the declarative workflow, loaded from disk.
- `SpikeAgentProvider.cs` — custom `ResponseAgentProvider` over our `IChatClient` (Goal 2 + MCP wiring).
- `FakeChatClient.cs` — deterministic offline LLM that emits a tool call then a final answer.
- `McpWordCountServer.cs` — real MCP server+client over in-memory pipes (Goal 3).
- `nuget.config` — nuget.org only (see Finding 1).
