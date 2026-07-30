using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "SuperAdmin")]
public class BrandsController : ControllerBase
{
    private readonly IBrandService _brandService;
    private readonly ILogger<BrandsController> _logger;

    public BrandsController(IBrandService brandService, ILogger<BrandsController> logger)
    {
        _brandService = brandService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetBrands(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var brands = await _brandService.GetAllAsync(includeInactive, cancellationToken);
        return Ok(brands);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveBrands(CancellationToken cancellationToken = default)
    {
        var brands = await _brandService.GetActiveBrandsAsync(cancellationToken);
        return Ok(brands);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBrand(Guid id, CancellationToken cancellationToken = default)
    {
        var brand = await _brandService.GetByIdAsync(id, cancellationToken);
        if (brand == null) return NotFound();
        return Ok(brand);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBrand([FromBody] CreateBrandRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var brand = await _brandService.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created brand {BrandId} - {Name}", brand.Id, brand.Name);
            return CreatedAtAction(nameof(GetBrand), new { id = brand.Id }, brand);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBrand(Guid id, [FromBody] UpdateBrandRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var brand = await _brandService.UpdateAsync(request, cancellationToken);
            if (brand == null) return NotFound();
            _logger.LogInformation("Updated brand {BrandId}", id);
            return Ok(brand);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBrand(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _brandService.DeleteAsync(id, cancellationToken);
            if (!result) return NotFound();
            _logger.LogInformation("Deleted brand {BrandId}", id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderBrands([FromBody] ReorderBrandsRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var indexedIds = request.Ids.Select((id, index) => (Id: id, Order: index)).ToList();
            await _brandService.ReorderAsync(indexedIds, cancellationToken);
            _logger.LogInformation("Reordered brands");
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class ReorderBrandsRequest
{
    public List<Guid> Ids { get; set; } = new();
}
