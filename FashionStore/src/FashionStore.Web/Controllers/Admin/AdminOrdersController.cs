using System.Security.Claims;
using System.Text.Json.Serialization;
using FashionStore.Application.Authorization;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative order management API. List, filter, export and inspect orders,
/// and drive the lifecycle through a single state machine service. Every action is
/// guarded by a dedicated permission policy (view / update status / cancel / add
/// note / print invoice), so roles such as OrderManager, CustomerSupport and Admin
/// receive different subsets of order capabilities.
/// </summary>
[ApiController]
[Route("api/admin/orders")]
public class AdminOrdersController : ControllerBase
{
    private readonly IOrderAdministrationService _orderAdministrationService;
    private readonly ILogger<AdminOrdersController> _logger;

    public AdminOrdersController(
        IOrderAdministrationService orderAdministrationService,
        ILogger<AdminOrdersController> logger)
    {
        _orderAdministrationService = orderAdministrationService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = OrderPolicies.OrdersView)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] AdminOrderQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderAdministrationService.GetOrdersAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("export")]
    [Authorize(Policy = OrderPolicies.OrdersView)]
    public async Task<IActionResult> Export(
        [FromQuery] AdminOrderQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderAdministrationService.ExportOrdersAsync(query, cancellationToken);
        return File(
            System.Text.Encoding.UTF8.GetBytes(result.Csv),
            "text/csv",
            result.FileName);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = OrderPolicies.OrdersView)]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _orderAdministrationService.GetOrderDetailAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = OrderPolicies.OrdersUpdateStatus)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _orderAdministrationService.UpdateOrderStatusAsync(
            id,
            request.ToStatus,
            request.Note,
            actorId,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error, orderNumber = result.OrderNumber });
        }

        return Ok(new
        {
            success = true,
            orderNumber = result.OrderNumber,
            orderStatus = result.NewOrderStatus,
            fulfilmentStatus = result.NewFulfilmentStatus,
            trackingNumber = result.TrackingNumber
        });
    }

    [HttpPost("{id:guid}/fulfilment")]
    [Authorize(Policy = OrderPolicies.OrdersUpdateStatus)]
    public async Task<IActionResult> UpdateFulfilment(
        Guid id,
        [FromBody] UpdateFulfilmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _orderAdministrationService.UpdateFulfilmentStatusAsync(
            id,
            request.ToStatus,
            request.Note,
            actorId,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error, orderNumber = result.OrderNumber });
        }

        return Ok(new
        {
            success = true,
            orderNumber = result.OrderNumber,
            orderStatus = result.NewOrderStatus,
            fulfilmentStatus = result.NewFulfilmentStatus
        });
    }

    [HttpPost("{id:guid}/pack")]
    [Authorize(Policy = OrderPolicies.OrdersUpdateStatus)]
    public async Task<IActionResult> Pack(Guid id, CancellationToken cancellationToken = default)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _orderAdministrationService.MarkAsPackedAsync(id, actorId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error, orderNumber = result.OrderNumber });
        }

        return Ok(new { success = true, orderNumber = result.OrderNumber });
    }

    [HttpPost("{id:guid}/ship")]
    [Authorize(Policy = OrderPolicies.OrdersUpdateStatus)]
    public async Task<IActionResult> Ship(
        Guid id,
        [FromBody] AdminShipRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _orderAdministrationService.MarkAsShippedAsync(id, request, actorId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error, orderNumber = result.OrderNumber });
        }

        return Ok(new
        {
            success = true,
            orderNumber = result.OrderNumber,
            orderStatus = result.NewOrderStatus,
            fulfilmentStatus = result.NewFulfilmentStatus,
            trackingNumber = result.TrackingNumber
        });
    }

    [HttpPost("{id:guid}/deliver")]
    [Authorize(Policy = OrderPolicies.OrdersUpdateStatus)]
    public async Task<IActionResult> Deliver(Guid id, CancellationToken cancellationToken = default)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _orderAdministrationService.MarkAsDeliveredAsync(id, actorId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error, orderNumber = result.OrderNumber });
        }

        return Ok(new
        {
            success = true,
            orderNumber = result.OrderNumber,
            orderStatus = result.NewOrderStatus,
            fulfilmentStatus = result.NewFulfilmentStatus,
            trackingNumber = result.TrackingNumber
        });
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = OrderPolicies.OrdersCancel)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _orderAdministrationService.CancelOrderAsync(id, request.Reason, actorId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error, orderNumber = result.OrderNumber });
        }

        return Ok(new { success = true, orderNumber = result.OrderNumber, orderStatus = result.NewOrderStatus });
    }

    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = OrderPolicies.OrdersAddNote)]
    public async Task<IActionResult> AddNote(
        Guid id,
        [FromBody] AddOrderNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
        var result = await _orderAdministrationService.AddNoteAsync(id, request, actorId, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error, orderNumber = result.OrderNumber });
        }

        return Ok(new { success = true, orderNumber = result.OrderNumber });
    }
}

public sealed record UpdateOrderStatusRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OrderStatus ToStatus,
    string? Note);

public sealed record UpdateFulfilmentRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] FulfilmentStatus ToStatus,
    string? Note);

public sealed record CancelOrderRequest(
    string? Reason);
