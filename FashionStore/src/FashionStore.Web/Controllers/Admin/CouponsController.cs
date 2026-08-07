using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _couponService;
    private readonly ILogger<CouponsController> _logger;

    public CouponsController(ICouponService couponService, ILogger<CouponsController> logger)
    {
        _couponService = couponService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCoupons(
        [FromQuery] string? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken = default)
    {
        var coupons = await _couponService.GetAllAsync(status, search, cancellationToken);
        return Ok(coupons);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] Guid? couponId,
        [FromQuery] string? userId,
        CancellationToken cancellationToken = default)
    {
        var usage = await _couponService.GetUsageAsync(couponId, userId, cancellationToken);
        return Ok(usage);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCoupon(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await _couponService.GetByIdAsync(id, cancellationToken);
        if (coupon == null) return NotFound();
        return Ok(coupon);
    }

    [HttpGet("code/{code}")]
    public async Task<IActionResult> CheckCode(string code, [FromQuery] Guid? excludeId, CancellationToken cancellationToken = default)
    {
        var isUnique = await _couponService.IsCodeUniqueAsync(code, excludeId, cancellationToken);
        return Ok(new { isUnique });
    }

    [HttpPost]
    public async Task<IActionResult> CreateCoupon([FromBody] CreateCouponRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var coupon = await _couponService.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created coupon {CouponId} - {Code}", coupon.Id, coupon.Code);
            return CreatedAtAction(nameof(GetCoupon), new { id = coupon.Id }, coupon);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCoupon(Guid id, [FromBody] UpdateCouponRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var coupon = await _couponService.UpdateAsync(id, request, cancellationToken);
            if (coupon == null) return NotFound();
            _logger.LogInformation("Updated coupon {CouponId}", id);
            return Ok(coupon);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> ToggleCoupon(Guid id, [FromBody] ToggleCouponRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _couponService.SetActiveAsync(id, request.IsActive, cancellationToken);
            if (!updated) return NotFound();
            _logger.LogInformation("{(State)} coupon {CouponId}", request.IsActive ? "Activated" : "Deactivated", id);
            return Ok(new { success = true, isActive = request.IsActive });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> DuplicateCoupon(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var copy = await _couponService.DuplicateAsync(id, cancellationToken);
            if (copy == null) return NotFound();
            _logger.LogInformation("Duplicated coupon {CouponId} into {CopyId}", id, copy.Id);
            return Ok(copy);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class ToggleCouponRequest
{
    public bool IsActive { get; set; }
}
