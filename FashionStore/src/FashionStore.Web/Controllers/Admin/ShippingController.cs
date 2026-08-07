using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative shipping configuration API. Access is restricted to the Admin and
/// SuperAdmin roles and every write is validated server-side by the shipping
/// service before it is persisted.
/// </summary>
[ApiController]
[Route("api/admin/shipping")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class ShippingController : ControllerBase
{
    private readonly IShippingService _shippingService;
    private readonly ILogger<ShippingController> _logger;

    public ShippingController(IShippingService shippingService, ILogger<ShippingController> logger)
    {
        _shippingService = shippingService;
        _logger = logger;
    }

    // ---- Shipping methods ----

    [HttpGet("methods")]
    public async Task<IActionResult> GetMethods(
        [FromQuery] bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var methods = await _shippingService.GetMethodsAsync(includeInactive, cancellationToken);
        return Ok(methods);
    }

    [HttpGet("methods/{id:guid}")]
    public async Task<IActionResult> GetMethod(Guid id, CancellationToken cancellationToken = default)
    {
        var method = await _shippingService.GetMethodByIdAsync(id, cancellationToken);
        return method == null ? NotFound() : Ok(method);
    }

    [HttpGet("methods/code/{code}")]
    public async Task<IActionResult> CheckCode(string code, [FromQuery] Guid? excludeId, CancellationToken cancellationToken = default)
    {
        var isUnique = await _shippingService.IsMethodCodeUniqueAsync(code, excludeId, cancellationToken);
        return Ok(new { isUnique });
    }

    [HttpPost("methods")]
    public async Task<IActionResult> CreateMethod([FromBody] CreateShippingMethodRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var method = await _shippingService.CreateMethodAsync(request, cancellationToken);
            _logger.LogInformation("Created shipping method {MethodId} - {Code}", method.Id, method.Code);
            return CreatedAtAction(nameof(GetMethod), new { id = method.Id }, method);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("methods/{id:guid}")]
    public async Task<IActionResult> UpdateMethod(Guid id, [FromBody] UpdateShippingMethodRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var method = await _shippingService.UpdateMethodAsync(id, request, cancellationToken);
            if (method == null) return NotFound();
            _logger.LogInformation("Updated shipping method {MethodId}", id);
            return Ok(method);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("methods/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleMethod(Guid id, [FromBody] ToggleShippingRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _shippingService.SetMethodActiveAsync(id, request.IsActive, cancellationToken);
            if (!updated) return NotFound();
            _logger.LogInformation("{(State)} shipping method {MethodId}", request.IsActive ? "Activated" : "Deactivated", id);
            return Ok(new { success = true, isActive = request.IsActive });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("methods/reorder")]
    public async Task<IActionResult> ReorderMethods([FromBody] IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        if (orderedIds == null || orderedIds.Count == 0) return BadRequest(new { error = "An ordered list of method ids is required." });
        try
        {
            var reordered = await _shippingService.ReorderMethodsAsync(orderedIds, cancellationToken);
            if (!reordered) return NotFound();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ---- Shipping zones ----

    [HttpGet("zones")]
    public async Task<IActionResult> GetZones(
        [FromQuery] bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var zones = await _shippingService.GetZonesAsync(includeInactive, cancellationToken);
        return Ok(zones);
    }

    [HttpGet("zones/{id:guid}")]
    public async Task<IActionResult> GetZone(Guid id, CancellationToken cancellationToken = default)
    {
        var zone = await _shippingService.GetZoneByIdAsync(id, cancellationToken);
        return zone == null ? NotFound() : Ok(zone);
    }

    [HttpPost("zones")]
    public async Task<IActionResult> CreateZone([FromBody] CreateShippingZoneRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var zone = await _shippingService.CreateZoneAsync(request, cancellationToken);
            _logger.LogInformation("Created shipping zone {ZoneId} - {Name}", zone.Id, zone.Name);
            return CreatedAtAction(nameof(GetZone), new { id = zone.Id }, zone);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("zones/{id:guid}")]
    public async Task<IActionResult> UpdateZone(Guid id, [FromBody] UpdateShippingZoneRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var zone = await _shippingService.UpdateZoneAsync(id, request, cancellationToken);
            if (zone == null) return NotFound();
            _logger.LogInformation("Updated shipping zone {ZoneId}", id);
            return Ok(zone);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("zones/{id:guid}/toggle")]
    public async Task<IActionResult> ToggleZone(Guid id, [FromBody] ToggleShippingRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _shippingService.SetZoneActiveAsync(id, request.IsActive, cancellationToken);
            if (!updated) return NotFound();
            _logger.LogInformation("{(State)} shipping zone {ZoneId}", request.IsActive ? "Activated" : "Deactivated", id);
            return Ok(new { success = true, isActive = request.IsActive });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("zones/{id:guid}")]
    public async Task<IActionResult> DeleteZone(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _shippingService.DeleteZoneAsync(id, cancellationToken);
            if (!deleted) return NotFound();
            _logger.LogInformation("Deleted shipping zone {ZoneId}", id);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ---- Shipping rates ----

    [HttpGet("rates")]
    public async Task<IActionResult> GetRates(
        [FromQuery] Guid? methodId,
        CancellationToken cancellationToken = default)
    {
        var rates = await _shippingService.GetRatesAsync(methodId, cancellationToken);
        return Ok(rates);
    }

    [HttpPost("rates")]
    public async Task<IActionResult> CreateRate([FromBody] CreateShippingRateRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var rate = await _shippingService.CreateRateAsync(request, cancellationToken);
            _logger.LogInformation("Created shipping rate {RateId} for method {MethodId}", rate.Id, rate.ShippingMethodId);
            return Ok(rate);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("rates/{id:guid}")]
    public async Task<IActionResult> UpdateRate(Guid id, [FromBody] UpdateShippingRateRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var rate = await _shippingService.UpdateRateAsync(id, request, cancellationToken);
            if (rate == null) return NotFound();
            _logger.LogInformation("Updated shipping rate {RateId}", id);
            return Ok(rate);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("rates/{id:guid}")]
    public async Task<IActionResult> DeleteRate(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _shippingService.DeleteRateAsync(id, cancellationToken);
            if (!deleted) return NotFound();
            _logger.LogInformation("Deleted shipping rate {RateId}", id);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ---- Delivery blackouts ----

    [HttpGet("blackouts")]
    public async Task<IActionResult> GetBlackouts(
        [FromQuery] Guid methodId,
        CancellationToken cancellationToken = default)
    {
        var blackouts = await _shippingService.GetBlackoutsAsync(methodId, cancellationToken);
        return Ok(blackouts);
    }

    [HttpPost("blackouts")]
    public async Task<IActionResult> CreateBlackout([FromBody] CreateDeliveryBlackoutRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var blackout = await _shippingService.CreateBlackoutAsync(request, cancellationToken);
            _logger.LogInformation("Created delivery blackout {BlackoutId} for method {MethodId}", blackout.Id, blackout.ShippingMethodId);
            return Ok(blackout);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("blackouts/{id:guid}")]
    public async Task<IActionResult> UpdateBlackout(Guid id, [FromBody] UpdateDeliveryBlackoutRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var blackout = await _shippingService.UpdateBlackoutAsync(id, request, cancellationToken);
            if (blackout == null) return NotFound();
            _logger.LogInformation("Updated delivery blackout {BlackoutId}", id);
            return Ok(blackout);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("blackouts/{id:guid}")]
    public async Task<IActionResult> DeleteBlackout(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _shippingService.DeleteBlackoutAsync(id, cancellationToken);
            if (!deleted) return NotFound();
            _logger.LogInformation("Deleted delivery blackout {BlackoutId}", id);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
