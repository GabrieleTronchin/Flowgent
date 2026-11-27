// ApplicationA — OrderController.cs
// Bug Scenario: Null reference exception when order items collection is null

namespace ApplicationA.Controllers;

using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly IOrderRepository _orderRepository;

    public OrderController(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    [HttpGet("{orderId}/summary")]
    public IActionResult GetOrderSummary(int orderId)
    {
        var order = _orderRepository.GetById(orderId);
        if (order == null)
            return NotFound();

        // BUG: order.Items can be null when the order has no items yet,
        // causing a NullReferenceException on the .Sum() call.
        var totalPrice = order.Items.Sum(item => item.Price * item.Quantity);
        var itemCount = order.Items.Count;

        return Ok(new OrderSummaryDto
        {
            OrderId = order.Id,
            CustomerName = order.CustomerName,
            TotalPrice = totalPrice,
            ItemCount = itemCount,
            Status = order.Status
        });
    }

    [HttpPost]
    public IActionResult CreateOrder([FromBody] CreateOrderRequest request)
    {
        var order = new Order
        {
            CustomerName = request.CustomerName,
            Status = "Pending",
            Items = null // Items are added later via a separate endpoint
        };

        _orderRepository.Add(order);
        return CreatedAtAction(nameof(GetOrderSummary), new { orderId = order.Id }, order);
    }
}
