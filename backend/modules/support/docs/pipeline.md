# Support pipeline

[← Support module](../README.md) · [Demo responses](demo-responses.md) · [Scenarios](scenarios.md) · [Endpoints](endpoints.md)

Definition: [`definition/workflows/support-email.yaml`](../definition/workflows/support-email.yaml) (workflow `support-email`, version 1). Run title: the email subject.

## Input

[`schemas/support-email.input.schema.json`](../definition/schemas/support-email.input.schema.json): `sender` (optional string), `subject` and `body` (required, at least one non-whitespace character). An invalid input is rejected with `400` and the message `Subject and Body are required.`, both by the module endpoint and by the engine intake.

## Steps

### 1. `classify` (llm)

- Prompts: [`classify.system.md`](../definition/prompts/classify.system.md); user prompt with subject and body. Temperature 0.1.
- Output ([`classification.schema.json`](../definition/schemas/classification.schema.json)): `isCodeRelated`, `category` (`BackendBug` | `FrontendBug` | `QualityTestIssue` | `OutOfScope`), `confidenceScore`, `reasoning`.
- Detail: `<category> (<confidence>%)`.
- Transitions: `isCodeRelated` → `route_team`; otherwise → `classified_out_of_scope`.

### 2. `route_team` (rule)

Matched against `subject + " " + body`, case-insensitive:

| Rule | Output |
|------|--------|
| `Application\s?A` | `{ teamName: TeamA, applicationName: ApplicationA, applicationFolder: application-a }` |
| `Application\s?B` | `{ teamName: TeamB, applicationName: ApplicationB, applicationFolder: application-b }` |

- Both match → the run fails: `Ambiguous routing: both ApplicationA and ApplicationB are mentioned.`
- None matches → the run fails: `Cannot determine affected application: neither ApplicationA nor ApplicationB is mentioned.`
- Detail: the team name. Next: `assign_agent`.

### 3. `assign_agent` (map)

| Category | Role |
|----------|------|
| `BackendBug` | `BackendDeveloper` |
| `FrontendBug` | `FrontendDeveloper` |
| `QualityTestIssue` | `QAEngineer` |

Outputs `role` and `agentId = <teamName>_<role>` (e.g. `TeamA_BackendDeveloper`). `assignAgent` makes it the run's assigned agent, shown as Working on the Agents page until the run ends. Detail: the agent id. Next: `locate_component`.

### 4. `locate_component` (tool `support.locateComponent`)

Input `{ applicationFolder, text }` (`text` = subject + body). Implemented in [`mcp/Flowgent.Mcp.Support/LocateComponentCore.cs`](../mcp/Flowgent.Mcp.Support/LocateComponentCore.cs) (served by the standalone support MCP server):

- Scans `<module>/<config.dummyAppsRoot>/<applicationFolder>` (folder name must match `^[a-z0-9-]+$` and stay inside the dummy-apps folder) for `.cs`, `.razor`, `.cshtml`, `.ts`, `.tsx`, `.js`, `.css` files.
- Picks the file whose name or stem (`OrderController`, `OrderSummary.razor`) appears first in the text.
- Returns `{ found, component, path, language, snippet }`; `path` is relative to the module folder, e.g. `dummy-apps/application-a/src/Controllers/OrderController.cs`; `snippet` is the file content (truncated at 4000 characters).
- Nothing mentioned: `found: false`, `component: UnknownComponent`, `path: dummy-apps/<folder>/src/UnknownComponent`.

Detail: the path. Next: `resolve`.

### 5. `resolve` (llm)

- Prompts: [`resolve.system.md`](../definition/prompts/resolve.system.md) (agent persona and team) and [`resolve.user.md`](../definition/prompts/resolve.user.md) (agent, role, subject, body and the located source file). Temperature 0.2. Attributed to the assigned agent.
- Output ([`resolution.schema.json`](../definition/schemas/resolution.schema.json)): `rootCauseDescription`, `affectedComponent`, `severityAssessment`, `proposedFixSummary`, `requiresEscalation`, `escalationReason`.
- Detail: the proposed fix. Next: `approval`.

### 6. `approval` (human-input)

Review title `Proposed fix: <affectedComponent>`; fields Root cause, Affected component, Severity, Proposed fix and Escalation (the reason, or `None`).

- `approved` → `generate_code`.
- `rejected` → `manual_review_required`.

### 7. `generate_code` (llm)

- Prompts: [`generate-code.system.md`](../definition/prompts/generate-code.system.md) and [`generate-code.user.md`](../definition/prompts/generate-code.user.md) (run id, resolution, file path and current content). Temperature 0.5. Attributed to the assigned agent.
- Output ([`pull-request.schema.json`](../definition/schemas/pull-request.schema.json)): `title`, `description`, `affectedFilePaths` (relative to the module folder), `simulatedDiff`.
- Detail: the title. Next: `code_change_generated`.

### Terminal steps

| Step | Outcome | Run status | Detail | Final output |
|------|---------|------------|--------|--------------|
| `code_change_generated` | success | Completed | Pull request title | The pull request |
| `classified_out_of_scope` | success | Completed | Classification reasoning | — |
| `manual_review_required` | failure | Failed | Rejection reason, or `Rejected` | — |

## Outcomes

| Situation | Final step | Status | Detail |
|-----------|------------|--------|--------|
| Code-related, approved | `code_change_generated` | Completed | `[Demo] Fix <component>` in Demo mode |
| Code-related, rejected | `manual_review_required` | Failed | Reason or `Rejected` |
| Not code-related | `classified_out_of_scope` | Completed | Reasoning |
| Both applications mentioned | `route_team` | Failed | `Ambiguous routing: …` |
| No application mentioned | `route_team` | Failed | `Cannot determine affected application: …` |
| Aborted | Current step | Failed | `Aborted by user` |
| Blank subject or body | — | `400`, no run | `Subject and Body are required.` |

These outcomes are asserted for every sample by `tests/SupportParityTests.cs`.
