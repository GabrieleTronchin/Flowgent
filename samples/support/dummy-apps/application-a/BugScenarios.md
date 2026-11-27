# Application A — Bug Scenarios

> Sample inputs: `scenario-a1`, `scenario-a2`, `scenario-a3` in [`definition/samples/scenarios.yaml`](../../definition/samples/scenarios.yaml). Expected results: [Scenarios](../../docs/scenarios.md) · [Support module](../../README.md)

## Scenario A1: Null Reference in Order Summary (Backend)

- **Category:** Backend Bug
- **Description:** The `GetOrderSummary` endpoint throws a `NullReferenceException` when `order.Items` is null. Orders created via `CreateOrder` start with `Items = null` because items are added later through a separate endpoint. Any call to `GetOrderSummary` before items are added crashes the API.
- **Affected File:** `src/Controllers/OrderController.cs`
- **Line Range:** 27–28
- **Buggy Code:**
  ```csharp
  var totalPrice = order.Items.Sum(item => item.Price * item.Quantity);
  var itemCount = order.Items.Count;
  ```
- **Expected Fix:**
  ```csharp
  var totalPrice = order.Items?.Sum(item => item.Price * item.Quantity) ?? 0m;
  var itemCount = order.Items?.Count ?? 0;
  ```

---

## Scenario A2: Incorrect Data Binding in Order Summary Component (Frontend)

- **Category:** Frontend Bug
- **Description:** The `OrderSummary.razor` component binds to `Order?.TotalCost`, but the `OrderSummaryDto` model defines the property as `TotalPrice`. The component renders nothing for the total price field because `TotalCost` does not exist on the DTO.
- **Affected File:** `src/Components/OrderSummary.razor`
- **Line Range:** 14
- **Buggy Code:**
  ```razor
  <p><strong>Total:</strong> @Order?.TotalCost</p>
  ```
- **Expected Fix:**
  ```razor
  <p><strong>Total:</strong> @Order?.TotalPrice</p>
  ```

---

## Scenario A3: Missing Test for Empty Order Edge Case (QA/Test)

- **Category:** Quality/Test Issue
- **Description:** The `OrderServiceTests` class has tests for orders with items but is missing a test for the edge case where an order has no items (`Items` is null or empty). This gap means the null reference bug in `OrderController.GetOrderSummary` goes undetected by the test suite.
- **Affected File:** `tests/OrderServiceTests.cs`
- **Line Range:** 35–44 (where the missing test should be)
- **Buggy Code:** (test is absent — only a comment placeholder exists)
  ```csharp
  // BUG: Missing test for empty order (zero items).
  ```
- **Expected Fix:** Add the following test:
  ```csharp
  [Fact]
  public void GetOrderSummary_WithEmptyOrder_ReturnsZeroTotal()
  {
      var service = new OrderService();
      var order = new Order { Id = 1, CustomerName = "Test", Items = null };
      var summary = service.GetOrderSummary(order);
      Assert.Equal(0m, summary.TotalPrice);
      Assert.Equal(0, summary.ItemCount);
  }
  ```
