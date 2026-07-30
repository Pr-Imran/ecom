using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "SuperAdmin")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(ICategoryService categoryService, ILogger<CategoriesController> logger)
    {
        _categoryService = categoryService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var categories = await _categoryService.GetAllAsync(includeInactive, cancellationToken);
        return Ok(categories);
    }

    [HttpGet("hierarchy")]
    public async Task<IActionResult> GetHierarchy(CancellationToken cancellationToken = default)
    {
        var hierarchy = await _categoryService.GetHierarchyAsync(cancellationToken);
        return Ok(hierarchy);
    }

    [HttpGet("menu")]
    public async Task<IActionResult> GetMenuCategories(CancellationToken cancellationToken = default)
    {
        var categories = await _categoryService.GetMenuCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCategory(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _categoryService.GetByIdAsync(id, cancellationToken);
        if (category == null) return NotFound();
        return Ok(category);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var category = await _categoryService.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created category {CategoryId} - {Name}", category.Id, category.Name);
            return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, category);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var category = await _categoryService.UpdateAsync(request, cancellationToken);
            if (category == null) return NotFound();
            _logger.LogInformation("Updated category {CategoryId}", id);
            return Ok(category);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _categoryService.DeleteAsync(id, cancellationToken);
            if (!result) return NotFound();
            _logger.LogInformation("Deleted category {CategoryId}", id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderCategories([FromBody] ReorderCategoriesRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var indexedIds = request.Ids.Select((id, index) => (Id: id, Order: index)).ToList();
            await _categoryService.ReorderAsync(indexedIds, cancellationToken);
            _logger.LogInformation("Reordered categories");
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class ReorderCategoriesRequest
{
    public List<Guid> Ids { get; set; } = new();
}
