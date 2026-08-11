using System.Security.Claims;
using System.Text.Json.Serialization;
using FashionStore.Application.Authorization;
using FashionStore.Application.DTOs.Returns;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative return management API. Every lifecycle action is guarded by a
/// dedicated permission policy and flows through the single admin state-machine
/// service, so roles such as Admin, OrderManager, CustomerSupport and
/// InventoryManager receive different subsets of return capabilities (review,
/// inspect, restock, refund, exchange, complete).
/// </summary>
[ApiController]
[Route("api/admin/returns")]
public class AdminReturnsController : ControllerBase
{
    private readonly IAdminReturnService _returnService;
    private readonly AppDbContext _context;
    private readonly ILogger<AdminReturnsController> _logger;

    public AdminReturnsController(
        IAdminReturnService returnService,
        AppDbContext context,
        ILogger<AdminReturnsController> logger)
    {
        _returnService = returnService;
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = ReturnPolicies.ReturnsView)]
    public async Task<IActionResult> GetReturns(
        [FromQuery] AdminReturnQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _returnService.GetReturnsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = ReturnPolicies.ReturnsView)]
    public async Task<IActionResult> GetReturn(Guid id, CancellationToken cancellationToken = default)
    {
        var returnRequest = await _returnService.GetReturnDetailAsync(id, cancellationToken);
        return returnRequest is null ? NotFound() : Ok(returnRequest);
    }

    /// <summary>
    /// Searches active catalogue variants for arranging an exchange replacement.
    /// Available to anyone who can arrange exchanges (Admin, OrderManager) without
    /// requiring the dedicated product-management endpoints.
    /// </summary>
    [HttpGet("variants")]
    [Authorize(Policy = ReturnPolicies.ReturnsExchange)]
    public async Task<IActionResult> SearchVariants([FromQuery] string? search, CancellationToken cancellationToken = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var query = _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product != null && v.Product.IsActive);

        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term.Replace("%", "[%]").Replace("_", "[_]")}%";
            query = query.Where(v =>
                EF.Functions.Like(v.Sku, pattern) ||
                EF.Functions.Like(v.Product!.Name, pattern) ||
                EF.Functions.Like(v.Product!.BaseSku, pattern));
        }

        var results = await query
            .OrderBy(v => v.Product!.Name)
            .ThenBy(v => v.Sku)
            .Take(25)
            .Select(v => new
            {
                v.Id,
                v.ProductId,
                v.Sku,
                v.Price,
                ProductName = v.Product!.Name,
                v.Product.BaseSku
            })
            .ToListAsync(cancellationToken);

        return Ok(results);
    }

    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = ReturnPolicies.ReturnsReview)]
    public async Task<IActionResult> Review(Guid id, [FromBody] UpdateReturnNotesRequest? request, CancellationToken cancellationToken = default)
    {
        return await RunTransitionAsync(
            () => _returnService.ReviewAsync(id, request?.Note, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = ReturnPolicies.ReturnsReview)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveReturnRequest? request, CancellationToken cancellationToken = default)
    {
        return await RunTransitionAsync(
            () => _returnService.ApproveAsync(id, request?.Note, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = ReturnPolicies.ReturnsReview)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectReturnRequest? request, CancellationToken cancellationToken = default)
    {
        return await RunTransitionAsync(
            () => _returnService.RejectAsync(id, request?.ReasonCode, request?.Note, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/receive")]
    [Authorize(Policy = ReturnPolicies.ReturnsReview)]
    public async Task<IActionResult> Receive(Guid id, [FromBody] MarkReceivedRequest? request, CancellationToken cancellationToken = default)
    {
        return await RunTransitionAsync(
            () => _returnService.MarkReceivedAsync(id, request?.Note, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/inspect")]
    [Authorize(Policy = ReturnPolicies.ReturnsInspect)]
    public async Task<IActionResult> Inspect(Guid id, [FromBody] InspectReturnRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "Inspection details are required." });
        }

        return await RunTransitionAsync(
            () => _returnService.InspectAsync(id, request, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/restock")]
    [Authorize(Policy = ReturnPolicies.ReturnsRestock)]
    public async Task<IActionResult> Restock(Guid id, [FromBody] RestockReturnItemRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "Restock details are required." });
        }

        return await RunTransitionAsync(
            () => _returnService.RestockItemAsync(id, request, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = ReturnPolicies.ReturnsRefund)]
    public async Task<IActionResult> Refund(Guid id, [FromBody] RefundReturnRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "Refund details are required." });
        }

        return await RunTransitionAsync(
            () => _returnService.RefundAsync(id, request, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/exchange")]
    [Authorize(Policy = ReturnPolicies.ReturnsExchange)]
    public async Task<IActionResult> Exchange(Guid id, [FromBody] ExchangeReturnRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "Exchange details are required." });
        }

        return await RunTransitionAsync(
            () => _returnService.ExchangeAsync(id, request, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = ReturnPolicies.ReturnsComplete)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteReturnRequest? request, CancellationToken cancellationToken = default)
    {
        return await RunTransitionAsync(
            () => _returnService.CompleteAsync(id, request?.Note, ActorId(), cancellationToken));
    }

    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = ReturnPolicies.ReturnsReview)]
    public async Task<IActionResult> UpdateNotes(Guid id, [FromBody] UpdateReturnNotesRequest? request, CancellationToken cancellationToken = default)
    {
        return await RunTransitionAsync(
            () => _returnService.UpdateNotesAsync(id, request?.Note, ActorId(), cancellationToken));
    }

    private async Task<IActionResult> RunTransitionAsync(Func<Task<ReturnTransitionResult>> action)
    {
        var result = await action();

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.ErrorMessage, returnNumber = result.ReturnNumber });
        }

        return Ok(new { success = true, returnNumber = result.ReturnNumber, status = result.Status });
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
}
