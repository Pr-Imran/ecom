using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class PromotionsController : ControllerBase
{
    private readonly IPromotionService _promotionService;
    private readonly ILogger<PromotionsController> _logger;

    public PromotionsController(IPromotionService promotionService, ILogger<PromotionsController> logger)
    {
        _promotionService = promotionService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetPromotions(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var promotions = await _promotionService.GetAllAsync(includeInactive, cancellationToken);
        return Ok(promotions);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetPromotion(Guid id, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionService.GetByIdAsync(id, cancellationToken);
        if (promotion == null) return NotFound();
        return Ok(promotion);
    }

    [HttpPost]
    public async Task<IActionResult> CreatePromotion([FromBody] CreatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var promotion = await _promotionService.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created promotion {PromotionId} - {Name}", promotion.Id, promotion.Name);
            return CreatedAtAction(nameof(GetPromotion), new { id = promotion.Id }, promotion);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdatePromotion(Guid id, [FromBody] UpdatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var promotion = await _promotionService.UpdateAsync(id, request, cancellationToken);
            if (promotion == null) return NotFound();
            _logger.LogInformation("Updated promotion {PromotionId}", id);
            return Ok(promotion);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> TogglePromotion(Guid id, [FromBody] TogglePromotionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _promotionService.SetActiveAsync(id, request.IsActive, cancellationToken);
            if (!updated) return NotFound();
            _logger.LogInformation("{(State)} promotion {PromotionId}", request.IsActive ? "Activated" : "Deactivated", id);
            return Ok(new { success = true, isActive = request.IsActive });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> DuplicatePromotion(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var copy = await _promotionService.DuplicateAsync(id, cancellationToken);
            if (copy == null) return NotFound();
            _logger.LogInformation("Duplicated promotion {PromotionId} into {CopyId}", id, copy.Id);
            return Ok(copy);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class TogglePromotionRequest
{
    public bool IsActive { get; set; }
}
