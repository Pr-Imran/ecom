using FashionStore.Application.Common.Exceptions;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _imageService;
    private readonly ILogger<ImagesController> _logger;

    public ImagesController(IImageService imageService, ILogger<ImagesController> logger)
    {
        _imageService = imageService;
        _logger = logger;
    }

    [HttpGet("product/{productId:guid}")]
    public async Task<IActionResult> GetProductImages(Guid productId, CancellationToken cancellationToken = default)
    {
        var images = await _imageService.GetProductImagesAsync(productId, cancellationToken);
        return Ok(images);
    }

    [HttpGet("variant/{variantId:guid}")]
    public async Task<IActionResult> GetVariantImages(Guid variantId, CancellationToken cancellationToken = default)
    {
        var images = await _imageService.GetVariantImagesAsync(variantId, cancellationToken);
        return Ok(images);
    }

    [HttpGet("product/{productId:guid}/count")]
    public async Task<IActionResult> GetProductImageCount(Guid productId, CancellationToken cancellationToken = default)
    {
        var count = await _imageService.GetProductImageCountAsync(productId, cancellationToken);
        return Ok(new { count });
    }

    [HttpPost("product/{productId:guid}")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadProductImage(
        Guid productId,
        [FromForm] ProductImageUploadForm form,
        CancellationToken cancellationToken = default)
    {
        if (form?.File == null || form.File.Length == 0)
        {
            return BadRequest(new { error = "A file is required" });
        }

        var request = new ProductImageUploadRequest(form.AltText, form.Caption, form.VariantId, form.IsMain);
        try
        {
            var image = await _imageService.UploadProductImageAsync(productId, ToUploadedFile(form.File), request, cancellationToken);
            _logger.LogInformation("Admin uploaded image {ImageId} for product {ProductId}", image.Id, productId);
            return CreatedAtAction(nameof(GetProductImages), new { productId }, image);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPost("variant/{variantId:guid}")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadVariantImage(
        Guid variantId,
        [FromForm] ProductImageUploadForm form,
        CancellationToken cancellationToken = default)
    {
        if (form?.File == null || form.File.Length == 0)
        {
            return BadRequest(new { error = "A file is required" });
        }

        var request = new ProductImageUploadRequest(form.AltText, form.Caption, variantId, form.IsMain);
        try
        {
            var image = await _imageService.UploadVariantImageAsync(variantId, ToUploadedFile(form.File), request, cancellationToken);
            _logger.LogInformation("Admin uploaded image {ImageId} for variant {VariantId}", image.Id, variantId);
            return CreatedAtAction(nameof(GetVariantImages), new { variantId }, image);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPost("category/{categoryId:guid}")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadCategoryImage(Guid categoryId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "A file is required" });
        }

        try
        {
            var url = await _imageService.UploadCategoryImageAsync(categoryId, ToUploadedFile(file), cancellationToken);
            _logger.LogInformation("Admin uploaded image for category {CategoryId}", categoryId);
            return Ok(new { url });
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPost("brand/{brandId:guid}")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadBrandImage(Guid brandId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "A file is required" });
        }

        try
        {
            var url = await _imageService.UploadBrandImageAsync(brandId, ToUploadedFile(file), cancellationToken);
            _logger.LogInformation("Admin uploaded image for brand {BrandId}", brandId);
            return Ok(new { url });
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPost("collection/{collectionId:guid}")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadCollectionImage(Guid collectionId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "A file is required" });
        }

        try
        {
            var url = await _imageService.UploadCollectionImageAsync(collectionId, ToUploadedFile(file), cancellationToken);
            _logger.LogInformation("Admin uploaded image for collection {CollectionId}", collectionId);
            return Ok(new { url });
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPut("{imageId:guid}/alt")]
    public async Task<IActionResult> UpdateAltText(Guid imageId, [FromBody] UpdateImageAltTextRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var image = await _imageService.UpdateAltTextAsync(imageId, request.AltText, cancellationToken);
            return Ok(image);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPut("{imageId:guid}/caption")]
    public async Task<IActionResult> UpdateCaption(Guid imageId, [FromBody] UpdateImageCaptionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var image = await _imageService.UpdateCaptionAsync(imageId, request.Caption, cancellationToken);
            return Ok(image);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPost("{imageId:guid}/main")]
    public async Task<IActionResult> SetMainImage(Guid imageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var image = await _imageService.SetMainImageAsync(imageId, cancellationToken);
            _logger.LogInformation("Admin set image {ImageId} as main", imageId);
            return Ok(image);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPut("{imageId:guid}/variant")]
    public async Task<IActionResult> AssignVariant(Guid imageId, [FromBody] AssignImageVariantRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var image = await _imageService.AssignVariantAsync(imageId, request.VariantId, cancellationToken);
            return Ok(image);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPut("product/{productId:guid}/reorder")]
    public async Task<IActionResult> Reorder(Guid productId, [FromBody] List<ImageOrderItem> items, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            return BadRequest(new { error = "Order items are required" });
        }

        try
        {
            await _imageService.ReorderProductImagesAsync(productId, items, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpPost("{imageId:guid}/replace")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> ReplaceImage(Guid imageId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "A file is required" });
        }

        try
        {
            var image = await _imageService.ReplaceImageAsync(imageId, ToUploadedFile(file), cancellationToken);
            _logger.LogInformation("Admin replaced image {ImageId}", imageId);
            return Ok(image);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpDelete("{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(Guid imageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _imageService.DeleteImageAsync(imageId, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { error = "Image not found" });
            }

            _logger.LogInformation("Admin deleted image {ImageId}", imageId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpDelete("category/{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategoryImage(Guid categoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _imageService.DeleteCategoryImageAsync(categoryId, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { error = "Category image not found" });
            }

            _logger.LogInformation("Admin deleted image for category {CategoryId}", categoryId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpDelete("brand/{brandId:guid}")]
    public async Task<IActionResult> DeleteBrandImage(Guid brandId, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _imageService.DeleteBrandImageAsync(brandId, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { error = "Brand image not found" });
            }

            _logger.LogInformation("Admin deleted image for brand {BrandId}", brandId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    [HttpDelete("collection/{collectionId:guid}")]
    public async Task<IActionResult> DeleteCollectionImage(Guid collectionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _imageService.DeleteCollectionImageAsync(collectionId, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { error = "Collection image not found" });
            }

            _logger.LogInformation("Admin deleted image for collection {CollectionId}", collectionId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResult(ex);
        }
    }

    private static UploadedFileInput ToUploadedFile(IFormFile file)
        => new(file.OpenReadStream(), file.FileName, file.ContentType, file.Length);

    private IActionResult ErrorResult(InvalidOperationException ex)
    {
        _logger.LogWarning("Image operation rejected: {Message}", ex.Message);
        var message = ex.Message ?? string.Empty;
        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(new { error = message });
        }

        return BadRequest(new { error = message });
    }
}

public sealed record UpdateImageAltTextRequest(string? AltText);

public sealed record UpdateImageCaptionRequest(string? Caption);

public sealed record AssignImageVariantRequest(Guid? VariantId);

public sealed class ProductImageUploadForm
{
    public IFormFile? File { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public Guid? VariantId { get; set; }
    public bool IsMain { get; set; }
}
