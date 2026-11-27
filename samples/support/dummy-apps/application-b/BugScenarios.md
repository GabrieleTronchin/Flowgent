# Application B — Bug Scenarios

> Sample inputs: `scenario-b1`, `scenario-b2`, `scenario-b3` in [`definition/samples/scenarios.yaml`](../../definition/samples/scenarios.yaml). Expected results: [Scenarios](../../docs/scenarios.md) · [Support module](../../README.md)

## Scenario B1: SQL Injection in User Search (Backend)

- **Category:** Backend Bug
- **Description:** The `SearchUsers` endpoint concatenates user input directly into a SQL query string without parameterization. An attacker can inject arbitrary SQL (e.g., `'; DROP TABLE Users; --`) to read, modify, or delete data.
- **Affected File:** `src/Controllers/UserController.cs`
- **Line Range:** 27
- **Buggy Code:**
  ```csharp
  var sql = "SELECT Id, DisplayName, Email FROM Users WHERE DisplayName LIKE '%" + query + "%'";
  ```
- **Expected Fix:**
  ```csharp
  var sql = "SELECT Id, DisplayName, Email FROM Users WHERE DisplayName LIKE @Query";
  // ...
  command.Parameters.AddWithValue("@Query", $"%{query}%");
  ```

---

## Scenario B2: Missing Null Check on Avatar URL (Frontend)

- **Category:** Frontend Bug
- **Description:** The `UserProfile.razor` component renders an `<img>` tag with `src="@User.AvatarUrl"` without checking for null. When `AvatarUrl` is null, the browser renders a broken image icon and makes a spurious HTTP request to the current page URL.
- **Affected File:** `src/Components/UserProfile.razor`
- **Line Range:** 11
- **Buggy Code:**
  ```razor
  <img src="@User.AvatarUrl" alt="@User.DisplayName" class="avatar" />
  ```
- **Expected Fix:**
  ```razor
  <img src="@(User.AvatarUrl ?? "/images/default-avatar.png")" alt="@User.DisplayName" class="avatar" />
  ```

---

## Scenario B3: Flaky Test Due to Hardcoded Date (QA/Test)

- **Category:** Quality/Test Issue
- **Description:** The `CreateUser_WithValidData_SetsCreatedDate` test asserts that the user's `CreatedAt.Date` equals `new DateTime(2024, 1, 15)`. This hardcoded date was valid only on the day the test was written. The test fails on every other day, making it flaky and unreliable in CI.
- **Affected File:** `tests/UserServiceTests.cs`
- **Line Range:** 18
- **Buggy Code:**
  ```csharp
  Assert.Equal(new DateTime(2024, 1, 15), user.CreatedAt.Date);
  ```
- **Expected Fix:**
  ```csharp
  Assert.Equal(DateTime.UtcNow.Date, user.CreatedAt.Date);
  ```
