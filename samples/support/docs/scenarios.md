# Scenarios and dummy apps

[← Support module](../README.md) · [Pipeline](pipeline.md) · [Endpoints](endpoints.md)

## Dummy apps

`dummy-apps/` (configured as `config.dummyAppsRoot` in `module.yaml`) contains two sample applications with deliberate bugs. They are the codebase the support agents work on: `support.locateComponent` reads their files and the generated pull requests point to them.

| Application | Folder | Domain | Files |
|-------------|--------|--------|-------|
| ApplicationA (TeamA) | [`dummy-apps/application-a`](../dummy-apps/application-a) | Orders | `src/Controllers/OrderController.cs`, `src/Components/OrderSummary.razor`, `tests/OrderServiceTests.cs` |
| ApplicationB (TeamB) | [`dummy-apps/application-b`](../dummy-apps/application-b) | Users | `src/Controllers/UserController.cs`, `src/Components/UserProfile.razor`, `tests/UserServiceTests.cs` |

Each folder has a `BugScenarios.md` describing its three bugs (description, affected file, line range, buggy code, expected fix):

- [`application-a/BugScenarios.md`](../dummy-apps/application-a/BugScenarios.md): A1 null reference in `GetOrderSummary`, A2 wrong binding in `OrderSummary.razor`, A3 missing empty-order test.
- [`application-b/BugScenarios.md`](../dummy-apps/application-b/BugScenarios.md): B1 SQL injection in `SearchUsers`, B2 missing null check on the avatar URL, B3 flaky test with a hard-coded date.

## Samples

[`definition/samples/scenarios.yaml`](../definition/samples/scenarios.yaml) is the single source of the ten scenarios. The dashboard shows them as templates (grouped by Application A, Application B, Edge Cases) through `GET /api/engine/workflows/support-email`, and [`service/support.http`](../service/support.http) is generated from them, so requests and templates cannot drift.

### Bug scenarios (Demo mode)

| Sample | Scenario | Category | Team | Agent | Component | Severity |
|--------|----------|----------|------|-------|-----------|----------|
| `scenario-a1` | A1: NullReferenceException | BackendBug | TeamA | `TeamA_BackendDeveloper` | `OrderController.cs` | High |
| `scenario-a2` | A2: Blank total price | FrontendBug | TeamA | `TeamA_FrontendDeveloper` | `OrderSummary.razor` | Medium |
| `scenario-a3` | A3: Missing test coverage | QualityTestIssue | TeamA | `TeamA_QAEngineer` | `OrderServiceTests.cs` | Medium |
| `scenario-b1` | B1: SQL Injection | BackendBug | TeamB | `TeamB_BackendDeveloper` | `UserController.cs` | Critical |
| `scenario-b2` | B2: Missing null check | FrontendBug | TeamB | `TeamB_FrontendDeveloper` | `UserProfile.razor` | Medium |
| `scenario-b3` | B3: Flaky test | QualityTestIssue | TeamB | `TeamB_QAEngineer` | `UserServiceTests.cs` | Medium |

Each one pauses at `approval`; approve → `code_change_generated` (Completed, `[Demo] Fix <component>`), reject → `manual_review_required` (Failed, reason or `Rejected`), abort → Failed `Aborted by user`.

### Edge cases

| Sample | Result |
|--------|--------|
| `edge-out-of-scope` (password reset request) | `classified_out_of_scope`, Completed |
| `edge-ambiguous-routing` (mentions both applications) | Failed at `route_team`: `Ambiguous routing: both ApplicationA and ApplicationB are mentioned.` |
| `edge-failed-routing` (mentions neither) | Failed at `route_team`: `Cannot determine affected application: neither ApplicationA nor ApplicationB is mentioned.` |
| `edge-empty-input` (blank subject and body, `expectInvalid: true`) | `400 Subject and Body are required.`, no run |

`tests/SupportParityTests.cs` runs every sample end-to-end through the gateway in Demo mode and asserts these results; `tests/ToolTests.cs` checks the located file for each bug scenario.

## Adding a scenario

1. Add the sample to `scenarios.yaml` (and, for a new bug, describe it in the application's `BugScenarios.md`).
2. Run `UPDATE_SNAPSHOTS=1 dotnet test --project samples/support/tests` (PowerShell: `$env:UPDATE_SNAPSHOTS=1; dotnet test --project samples/support/tests`) to regenerate `support.http`.
3. Extend the parity test data if the scenario has a new expected outcome.
