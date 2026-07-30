using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "SuperAdmin")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionService _collectionService;
    private readonly ILogger<CollectionsController> _logger;

    public CollectionsController(ICollectionService collectionService, ILogger<CollectionsController> logger)
    {
        _collectionService = collectionService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCollections(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var collections = await _collectionService.GetAllAsync(includeInactive, cancellationToken);
        return Ok(collections);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveCollections(CancellationToken cancellationToken = default)
    {
        var collections = await _collectionService.GetActiveCollectionsAsync(cancellationToken);
        return Ok(collections);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCollection(Guid id, CancellationToken cancellationToken = default)
    {
        var collection = await _collectionService.GetByIdAsync(id, cancellationToken);
        if (collection == null) return NotFound();
        return Ok(collection);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCollection([FromBody] CreateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var collection = await _collectionService.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created collection {CollectionId} - {Name}", collection.Id, collection.Name);
            return CreatedAtAction(nameof(GetCollection), new { id = collection.Id }, collection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCollection(Guid id, [FromBody] UpdateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var collection = await _collectionService.UpdateAsync(request, cancellationToken);
            if (collection == null) return NotFound();
            _logger.LogInformation("Updated collection {CollectionId}", id);
            return Ok(collection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCollection(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _collectionService.DeleteAsync(id, cancellationToken);
            if (!result) return NotFound();
            _logger.LogInformation("Deleted collection {CollectionId}", id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderCollections([FromBody] ReorderCollectionsRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var indexedIds = request.Ids.Select((id, index) => (Id: id, Order: index)).ToList();
            await _collectionService.ReorderAsync(indexedIds, cancellationToken);
            _logger.LogInformation("Reordered collections");
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class ReorderCollectionsRequest
{
    public List<Guid> Ids { get; set; } = new();
}
