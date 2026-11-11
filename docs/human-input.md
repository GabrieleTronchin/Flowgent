# Human input

[← README](../README.md) · [Workflow reference](workflow-reference.md#human-input) · [Engine API](engine-api.md#human-input)

A human-input gate pauses a run until someone approves or rejects it. The engine handles it generically, so every workflow uses the same endpoints and the same dashboard page.

## In the workflow

A gate is a MAF `Question` action followed by a `ConditionGroup` on the resumed answer (the declarative `RequestExternalInput`/`If` pair stalls after resume in this builder version, so `Question`/`ConditionGroup` are used instead). The `Question` prompt is a JSON review payload the engine turns into the dashboard review (title + ordered fields); the resume carries JSON `{decision, reason}`, read back with the built-in `flowgent.parse` tool.

```yaml
- kind: SetVariable
  id: review_json
  variable: Local.reviewJson
  value: =Concatenate("{""title"":""Publish summary?"",""fields"":[{""label"":""Summary"",""value"":""", Local.summary, """}]}")
- kind: Question
  id: review
  property: Local.approvalRaw
  prompt: { kind: Message, text: [ "{Local.reviewJson}" ] }
  entity: { kind: StringPrebuiltEntity }
- kind: InvokeMcpTool
  id: parse_review
  serverUrl: inproc://flowgent
  serverLabel: flowgent
  toolName: flowgent.parse
  requireApproval: false
  arguments: { payload: =Local.approvalRaw }
  output: { result: Local.approvalObj }
- kind: ConditionGroup
  id: after_review
  conditions:
    - condition: =Find("approved", Local.approvalRaw) > 0
      id: approved
      actions: [ { kind: GotoAction, id: goto_publish, actionId: publish } ]
  elseActions: [ { kind: GotoAction, id: goto_declined, actionId: declined_message } ]
```

The engine:

1. Builds the review payload (title + ordered fields) from the `Question` prompt JSON.
2. Records an `AwaitingInput` transition; the run status becomes `AwaitingInput`. Its detail is the prompt payload.
3. Waits for a decision, then records `InputReceived` (detail `approved` or `rejected: <reason>`) and resumes the workflow with the `{decision, reason}` JSON.

What happens after a rejection is up to the workflow: typically a `failure` terminal whose message uses the reason, e.g. `=Coalesce(First(Local.approvalObj).reason, "Rejected")`.

## API

| Method | Route | Result |
|--------|-------|--------|
| GET | `/api/engine/human-inputs?moduleId=&workflowId=` | Pending inputs with `runId`, `stepId`, `runTitle`, `review` and `requestedAt` |
| POST | `/api/engine/human-inputs/{runId}/approve` | `204` |
| POST | `/api/engine/human-inputs/{runId}/reject` | `204`; body `{ "reason": "..." }` optional |
| POST | `/api/engine/runs/{runId}/abort` | `202`; the run ends in `Failed` with `Aborted by user` |

`404` means the run does not exist; `409` means it is not awaiting input or has already completed.

## In the dashboard

The **Approvals** page lists the pending inputs of every module with their review fields, workflow and step, and offers **Approve**, **Reject** (with an optional reason) and **Abort**. The list refreshes on stream updates and every 5 seconds.

## Notes

- Pending inputs live in engine memory (`HumanInputRegistry`), like runs and events (EF Core InMemory): restarting the engine loses them.
- Abort and engine shutdown release the wait: abort ends the run in `Failed` with a single transition, shutdown ends it in `Cancelled`.
- Runs paused on human input do not block the inbox; in sequential mode they do block the next run from starting.
