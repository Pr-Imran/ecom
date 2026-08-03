using FashionStore.Application.Authorization;
using FashionStore.Application.DTOs.Inventory;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/inventory")]
[Authorize(Policy = InventoryPolicies.InventoryManage)]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(IInventoryService inventoryService, ILogger<InventoryController> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? searchTerm,
        [FromQuery] bool? lowStockOnly,
        [FromQuery] bool? outOfStockOnly,
        [FromQuery] bool? backorderOnly,
        [FromQuery] Guid? warehouseId,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDescending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new InventorySearchRequest(
            searchTerm, lowStockOnly, outOfStockOnly, backorderOnly, warehouseId,
            sortBy, sortDescending, Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
        var result = await _inventoryService.SearchInventoryAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken = default)
    {
        var summary = await _inventoryService.GetSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpGet("variants/{variantId:guid}")]
    public async Task<IActionResult> GetVariant(Guid variantId, CancellationToken cancellationToken = default)
    {
        var detail = await _inventoryService.GetVariantInventoryAsync(variantId, cancellationToken);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("variants/{variantId:guid}/transactions")]
    public async Task<IActionResult> GetTransactions(
        Guid variantId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var transactions = await _inventoryService.GetTransactionHistoryAsync(variantId, warehouseId, Math.Clamp(limit, 1, 500), cancellationToken);
        return Ok(transactions);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? searchTerm,
        [FromQuery] bool? lowStockOnly,
        [FromQuery] bool? outOfStockOnly,
        [FromQuery] bool? backorderOnly,
        [FromQuery] Guid? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var request = new InventorySearchRequest(
            searchTerm, lowStockOnly, outOfStockOnly, backorderOnly, warehouseId,
            null, false, 1, 1000);
        var csv = await _inventoryService.ExportInventoryCsvAsync(request, cancellationToken);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", $"inventory-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    [HttpPost("adjust")]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var stock = await _inventoryService.AdjustStockAsync(request, cancellationToken);
            return Ok(stock);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("bulk-adjust")]
    public async Task<IActionResult> BulkAdjustStock([FromBody] BulkAdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _inventoryService.BulkAdjustStockAsync(request, cancellationToken);
            return Ok(new { adjustedCount = count });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("variants/{variantId:guid}/thresholds")]
    public async Task<IActionResult> SetThresholds(Guid variantId, [FromBody] SetStockThresholdsRequest request, CancellationToken cancellationToken = default)
    {
        if (variantId != request.VariantId) return BadRequest(new { error = "Variant ID mismatch" });
        try
        {
            var stock = await _inventoryService.SetStockThresholdsAsync(request, cancellationToken);
            if (stock == null) return NotFound();
            return Ok(stock);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reservations")]
    public async Task<IActionResult> CreateReservation([FromBody] CreateStockReservationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var reservation = await _inventoryService.ReserveStockAsync(request, cancellationToken);
            return Ok(reservation);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reservations/{id:guid}/release")]
    public async Task<IActionResult> ReleaseReservation(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var released = await _inventoryService.ReleaseReservationAsync(id, cancellationToken);
            if (!released) return NotFound();
            return Ok(new { released = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reservations/release-expired")]
    public async Task<IActionResult> ReleaseExpired(CancellationToken cancellationToken = default)
    {
        var released = await _inventoryService.ReleaseExpiredReservationsAsync(cancellationToken);
        return Ok(new { releasedCount = released });
    }

    [HttpGet("warehouses")]
    public async Task<IActionResult> GetWarehouses([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var warehouses = await _inventoryService.GetWarehousesAsync(includeInactive, cancellationToken);
        return Ok(warehouses);
    }

    [HttpGet("warehouses/{id:guid}")]
    public async Task<IActionResult> GetWarehouse(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _inventoryService.GetWarehouseByIdAsync(id, cancellationToken);
        if (warehouse == null) return NotFound();
        return Ok(warehouse);
    }

    [HttpPost("warehouses")]
    public async Task<IActionResult> CreateWarehouse([FromBody] CreateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var warehouse = await _inventoryService.CreateWarehouseAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetWarehouse), new { id = warehouse.Id }, warehouse);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("warehouses/{id:guid}")]
    public async Task<IActionResult> UpdateWarehouse(Guid id, [FromBody] UpdateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        if (id != request.Id) return BadRequest(new { error = "Warehouse ID mismatch" });
        try
        {
            var warehouse = await _inventoryService.UpdateWarehouseAsync(request, cancellationToken);
            if (warehouse == null) return NotFound();
            return Ok(warehouse);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("warehouses/{id:guid}")]
    public async Task<IActionResult> DeleteWarehouse(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _inventoryService.DeleteWarehouseAsync(id, cancellationToken);
            if (!deleted) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
