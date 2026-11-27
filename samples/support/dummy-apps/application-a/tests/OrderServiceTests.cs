// ApplicationA — OrderServiceTests.cs
// Bug Scenario: Missing test for edge case — empty order (zero items)

namespace ApplicationA.Tests;

using Xunit;

public class OrderServiceTests
{
    [Fact]
    public void GetOrderSummary_WithValidOrder_ReturnsCorrectTotal()
    {
        // Arrange
        var service = new OrderService();
        var order = CreateOrderWithItems(3, pricePerItem: 10.00m, quantityPerItem: 2);

        // Act
        var summary = service.GetOrderSummary(order);

        // Assert
        Assert.Equal(60.00m, summary.TotalPrice);
        Assert.Equal(3, summary.ItemCount);
    }

    [Fact]
    public void GetOrderSummary_WithSingleItem_ReturnsCorrectTotal()
    {
        // Arrange
        var service = new OrderService();
        var order = CreateOrderWithItems(1, pricePerItem: 25.50m, quantityPerItem: 1);

        // Act
        var summary = service.GetOrderSummary(order);

        // Assert
        Assert.Equal(25.50m, summary.TotalPrice);
        Assert.Equal(1, summary.ItemCount);
    }

    // BUG: Missing test for empty order (zero items).
    // The OrderService.GetOrderSummary method throws a NullReferenceException
    // when order.Items is null, but there is no test covering this edge case.
    // The following test SHOULD exist but is absent:
    //
    // [Fact]
    // public void GetOrderSummary_WithEmptyOrder_ReturnsZeroTotal()
    // {
    //     var service = new OrderService();
    //     var order = new Order { Id = 1, CustomerName = "Test", Items = null };
    //     var summary = service.GetOrderSummary(order);
    //     Assert.Equal(0m, summary.TotalPrice);
    //     Assert.Equal(0, summary.ItemCount);
    // }

    private static Order CreateOrderWithItems(int count, decimal pricePerItem, int quantityPerItem)
    {
        var items = new List<OrderItem>();
        for (int i = 0; i < count; i++)
        {
            items.Add(new OrderItem
            {
                ProductName = $"Product {i + 1}",
                Price = pricePerItem,
                Quantity = quantityPerItem
            });
        }

        return new Order
        {
            Id = 1,
            CustomerName = "John Doe",
            Status = "Confirmed",
            Items = items
        };
    }
}
