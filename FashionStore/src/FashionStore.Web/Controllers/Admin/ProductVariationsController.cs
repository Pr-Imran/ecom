using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class ProductVariationsController : ControllerBase
{
    private readonly IProductVariationService _variationService;
    private readonly ILogger<ProductVariationsController> _logger;

    public ProductVariationsController(IProductVariationService variationService, ILogger<ProductVariationsController> logger)
    {
        _variationService = variationService;
        _logger = logger;
    }

    [HttpGet("attributes")]
    public async Task<IActionResult> GetAttributes(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var attributes = await _variationService.GetVariationAttributesAsync(includeInactive, cancellationToken);
        return Ok(attributes);
    }

    [HttpGet("attributes/{id:guid}")]
    public async Task<IActionResult> GetAttribute(Guid id, CancellationToken cancellationToken = default)
    {
        var attribute = await _variationService.GetAttributeByIdAsync(id, cancellationToken);
        if (attribute == null) return NotFound();
        return Ok(attribute);
    }

    [HttpPost("attributes")]
    public async Task<IActionResult> CreateAttribute([FromBody] CreateProductAttributeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var attribute = await _variationService.CreateAttributeAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetAttribute), new { id = attribute.Id }, attribute);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("attributes/{id:guid}")]
    public async Task<IActionResult> UpdateAttribute(Guid id, [FromBody] UpdateProductAttributeRequest request, CancellationToken cancellationToken = default)
    {
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var attribute = await _variationService.UpdateAttributeAsync(request, cancellationToken);
            if (attribute == null) return NotFound();
            return Ok(attribute);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("attributes/{id:guid}")]
    public async Task<IActionResult> DeleteAttribute(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _variationService.DeleteAttributeAsync(id, cancellationToken);
            if (!result) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("attribute-values")]
    public async Task<IActionResult> CreateAttributeValue([FromBody] CreateProductAttributeValueRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _variationService.CreateAttributeValueAsync(request, cancellationToken);
            return Ok(value);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("attribute-values/{id:guid}")]
    public async Task<IActionResult> UpdateAttributeValue(Guid id, [FromBody] UpdateProductAttributeValueRequest request, CancellationToken cancellationToken = default)
    {
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var value = await _variationService.UpdateAttributeValueAsync(request, cancellationToken);
            if (value == null) return NotFound();
            return Ok(value);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("attribute-values/{id:guid}")]
    public async Task<IActionResult> DeleteAttributeValue(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _variationService.DeleteAttributeValueAsync(id, cancellationToken);
            if (!result) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("products/{productId:guid}/variants")]
    public async Task<IActionResult> GetVariants(Guid productId, CancellationToken cancellationToken = default)
    {
        var variants = await _variationService.GetVariantsByProductAsync(productId, cancellationToken);
        return Ok(variants);
    }

    [HttpGet("products/{productId:guid}/storefront-variations")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStorefrontVariations(Guid productId, CancellationToken cancellationToken = default)
    {
        var variations = await _variationService.GetStorefrontVariationsAsync(productId, cancellationToken);
        return Ok(variations);
    }

    [HttpGet("products/{productId:guid}/variant-by-values")]
    [AllowAnonymous]
    public async Task<IActionResult> GetVariantByValues(Guid productId, [FromQuery] List<Guid> attributeValueIds, CancellationToken cancellationToken = default)
    {
        var variant = await _variationService.GetVariantByAttributeValuesAsync(productId, attributeValueIds, cancellationToken);
        if (variant == null) return NotFound();
        return Ok(variant);
    }

    [HttpGet("variants/{id:guid}")]
    public async Task<IActionResult> GetVariant(Guid id, CancellationToken cancellationToken = default)
    {
        var variant = await _variationService.GetVariantByIdAsync(id, cancellationToken);
        if (variant == null) return NotFound();
        return Ok(variant);
    }

    [HttpPost("variants")]
    public async Task<IActionResult> CreateVariant([FromBody] CreateProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var variant = await _variationService.CreateVariantAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetVariant), new { id = variant.Id }, variant);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("variants/{id:guid}")]
    public async Task<IActionResult> UpdateVariant(Guid id, [FromBody] UpdateProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        if (id != request.Id) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var variant = await _variationService.UpdateVariantAsync(request, cancellationToken);
            if (variant == null) return NotFound();
            return Ok(variant);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("variants/{id:guid}")]
    public async Task<IActionResult> DeleteVariant(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _variationService.DeleteVariantAsync(id, cancellationToken);
            if (!result) return NotFound();
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("products/{productId:guid}/variants/generate")]
    public async Task<IActionResult> GenerateVariants(Guid productId, [FromBody] GenerateVariantsRequest request, CancellationToken cancellationToken = default)
    {
        if (productId != request.ProductId) return BadRequest(new { error = "ID mismatch" });
        try
        {
            var combinations = await _variationService.GenerateCombinationsAsync(request, cancellationToken);
            return Ok(combinations);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("products/{productId:guid}/variants/save-generated")]
    public async Task<IActionResult> SaveGeneratedVariants(Guid productId, [FromBody] List<CreateProductVariantRequest> variants, CancellationToken cancellationToken = default)
    {
        try
        {
            await _variationService.SaveGeneratedVariantsAsync(variants, cancellationToken);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("variants/bulk-update")]
    public async Task<IActionResult> BulkUpdateVariants([FromBody] BulkUpdateVariantsRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await _variationService.BulkUpdateVariantsAsync(request, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("variants/sku-unique")]
    public async Task<IActionResult> IsSkuUnique([FromQuery] string sku, [FromQuery] Guid? excludeVariantId, CancellationToken cancellationToken = default)
    {
        var isUnique = await _variationService.IsSkuUniqueAsync(sku, excludeVariantId, cancellationToken);
        return Ok(new { isUnique });
    }
}
