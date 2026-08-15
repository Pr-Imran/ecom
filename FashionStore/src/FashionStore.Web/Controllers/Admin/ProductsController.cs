using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductService productService, IAuditService auditService, ILogger<ProductsController> logger)
    {
        _productService = productService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? searchTerm,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? brandId,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isFeatured,
        [FromQuery] string? sortBy = "newest",
        [FromQuery] bool sortDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ProductSearchRequest(
            searchTerm,
            categoryId,
            brandId,
            isActive,
            isFeatured,
            sortBy,
            sortDescending,
            page,
            pageSize
        );

        var result = await _productService.SearchAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);
        if (product == null) return NotFound();
        return Ok(product);
    }

    [HttpPost]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var product = await _productService.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created product {ProductId} - {Name}", product.Id, product.Name);
            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var product = await _productService.UpdateAsync(request, cancellationToken);
            if (product == null) return NotFound();
            _logger.LogInformation("Updated product {ProductId}", id);

            await _auditService.RecordAsync(
                "Product.Updated",
                "Product",
                id.ToString(),
                oldValue: $"price:{request.BasePrice:0.00}",
                newValue: $"price:{product.BasePrice:0.00}",
                cancellationToken: cancellationToken);

            return Ok(product);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _productService.DeleteAsync(id, cancellationToken);
            if (!result) return NotFound();
            _logger.LogInformation("Deleted product {ProductId}", id);

            await _auditService.RecordAsync(
                "Product.Deleted",
                "Product",
                id.ToString(),
                oldValue: "true",
                newValue: "false",
                cancellationToken: cancellationToken);

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> DuplicateProduct(Guid id, [FromBody] DuplicateProductRequest request, CancellationToken cancellationToken = default)
    {
        if (id != request.SourceProductId) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var product = await _productService.DuplicateAsync(request, cancellationToken);
            _logger.LogInformation("Duplicated product {SourceId} to {NewId}", id, product.Id);
            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> PublishProduct(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _productService.PublishAsync(id, cancellationToken);
            if (!result) return NotFound();
            _logger.LogInformation("Published product {ProductId}", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> ArchiveProduct(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _productService.ArchiveAsync(id, cancellationToken);
            if (!result) return NotFound();
            _logger.LogInformation("Archived product {ProductId}", id);

            await _auditService.RecordAsync(
                "Product.Archived",
                "Product",
                id.ToString(),
                oldValue: "true",
                newValue: "false",
                cancellationToken: cancellationToken);

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeaturedProducts([FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        var products = await _productService.GetFeaturedProductsAsync(count, cancellationToken);
        return Ok(products);
    }

    [HttpGet("new-arrivals")]
    public async Task<IActionResult> GetNewArrivals([FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        var products = await _productService.GetNewArrivalsAsync(count, cancellationToken);
        return Ok(products);
    }

    [HttpGet("best-sellers")]
    public async Task<IActionResult> GetBestSellers([FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        var products = await _productService.GetBestSellersAsync(count, cancellationToken);
        return Ok(products);
    }
}
