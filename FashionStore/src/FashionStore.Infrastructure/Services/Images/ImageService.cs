using FashionStore.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace FashionStore.Infrastructure.Services.Images;

public sealed class ImageService : IImageService
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IImageValidationService _validation;
    private readonly IImageProcessingService _processing;
    private readonly IImageProcessingDispatcher _queue;
    private readonly ImageSettings _settings;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ImageService> _logger;

    public ImageService(
        AppDbContext context,
        IFileStorageService storage,
        IImageValidationService validation,
        IImageProcessingService processing,
        IImageProcessingDispatcher queue,
        ImageSettings settings,
        IDistributedCache cache,
        ILogger<ImageService> logger)
    {
        _context = context;
        _storage = storage;
        _validation = validation;
        _processing = processing;
        _queue = queue;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<ProductImageDto>> GetProductImagesAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var images = await _context.ProductImages
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return images.Select(ToDto);
    }

    public async Task<IEnumerable<ProductImageDto>> GetVariantImagesAsync(Guid variantId, CancellationToken cancellationToken = default)
    {
        var images = await _context.ProductImages
            .Where(i => i.ProductVariantId == variantId)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return images.Select(ToDto);
    }

    public async Task<ProductImageDto> UploadProductImageAsync(
        Guid productId,
        UploadedFileInput file,
        ProductImageUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync(new object[] { productId }, cancellationToken);
        if (product == null)
        {
            throw new InvalidOperationException("Product not found");
        }

        var imageCount = await _context.ProductImages.CountAsync(i => i.ProductId == productId, cancellationToken);
        if (imageCount >= _settings.MaxImageCountPerProduct)
        {
            throw new InvalidOperationException($"Maximum of {_settings.MaxImageCountPerProduct} images per product reached");
        }

        if (request.VariantId.HasValue &&
            !await _context.ProductVariants.AnyAsync(v => v.Id == request.VariantId.Value && v.ProductId == productId, cancellationToken))
        {
            throw new InvalidOperationException("Variant does not belong to this product");
        }

        var validation = await ValidateAsync(file, cancellationToken);

        var extension = GetExtension(validation.DetectedFormat!);
        var relativePath = FileStoragePath.Combine($"products/{productId:N}", $"{Guid.NewGuid():N}{extension}");
        await _storage.SaveAsync(relativePath, file.Content, validation.NormalizedContentType!, cancellationToken);

        var isMain = request.IsMain || !await _context.ProductImages.AnyAsync(i => i.ProductId == productId && i.IsMain, cancellationToken);
        if (isMain)
        {
            await ClearMainForProductAsync(productId, cancellationToken);
        }

        var image = new ProductImage
        {
            ProductId = productId,
            ProductVariantId = request.VariantId,
            FileName = relativePath,
            OriginalFileName = file.OriginalFileName,
            AltText = request.AltText,
            Caption = request.Caption,
            IsMain = isMain,
            DisplayOrder = imageCount,
            ImageFormat = validation.DetectedFormat!,
            ContentType = validation.NormalizedContentType!,
            SizeBytes = file.Length,
            Width = validation.Width,
            Height = validation.Height,
            ProcessingStatus = "Pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.ProductImages.Add(image);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        await _queue.EnqueueAsync(new ImageProcessingJob(image.Id, relativePath), cancellationToken);

        _logger.LogInformation("Uploaded product image {ImageId} for product {ProductId}", image.Id, productId);
        return ToDto(image);
    }

    public async Task<ProductImageDto> UploadVariantImageAsync(
        Guid variantId,
        UploadedFileInput file,
        ProductImageUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var variant = await _context.ProductVariants.FindAsync(new object[] { variantId }, cancellationToken);
        if (variant == null)
        {
            throw new InvalidOperationException("Variant not found");
        }

        return await UploadProductImageAsync(
            variant.ProductId,
            file,
            request with { VariantId = variantId },
            cancellationToken);
    }

    public async Task<string> UploadCategoryImageAsync(Guid categoryId, UploadedFileInput file, CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories.FindAsync(new object[] { categoryId }, cancellationToken);
        if (category == null)
        {
            throw new InvalidOperationException("Category not found");
        }

        return await UploadReferencedImageAsync(
            $"categories/{categoryId:N}",
            file,
            async url =>
            {
                var oldUrl = category.ImageUrl;
                category.ImageUrl = url;
                category.UpdatedAtUtc = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(oldUrl) && oldUrl != url)
                {
                    await DeleteStoredFileByUrlAsync(oldUrl, cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task<string> UploadBrandImageAsync(Guid brandId, UploadedFileInput file, CancellationToken cancellationToken = default)
    {
        var brand = await _context.Brands.FindAsync(new object[] { brandId }, cancellationToken);
        if (brand == null)
        {
            throw new InvalidOperationException("Brand not found");
        }

        return await UploadReferencedImageAsync(
            $"brands/{brandId:N}",
            file,
            async url =>
            {
                var oldUrl = brand.LogoUrl;
                brand.LogoUrl = url;
                brand.UpdatedAtUtc = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(oldUrl) && oldUrl != url)
                {
                    await DeleteStoredFileByUrlAsync(oldUrl, cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task<string> UploadCollectionImageAsync(Guid collectionId, UploadedFileInput file, CancellationToken cancellationToken = default)
    {
        var collection = await _context.Collections.FindAsync(new object[] { collectionId }, cancellationToken);
        if (collection == null)
        {
            throw new InvalidOperationException("Collection not found");
        }

        return await UploadReferencedImageAsync(
            $"collections/{collectionId:N}",
            file,
            async url =>
            {
                var oldUrl = collection.BannerImageUrl;
                collection.BannerImageUrl = url;
                collection.UpdatedAtUtc = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(oldUrl) && oldUrl != url)
                {
                    await DeleteStoredFileByUrlAsync(oldUrl, cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task<ProductImageDto> UpdateAltTextAsync(Guid imageId, string? altText, CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages.FindAsync(new object[] { imageId }, cancellationToken);
        if (image == null)
        {
            throw new InvalidOperationException("Image not found");
        }

        image.AltText = altText;
        image.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        return ToDto(image);
    }

    public async Task<ProductImageDto> UpdateCaptionAsync(Guid imageId, string? caption, CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages.FindAsync(new object[] { imageId }, cancellationToken);
        if (image == null)
        {
            throw new InvalidOperationException("Image not found");
        }

        image.Caption = caption;
        image.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        return ToDto(image);
    }

    public async Task<ProductImageDto> SetMainImageAsync(Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages.FindAsync(new object[] { imageId }, cancellationToken);
        if (image == null)
        {
            throw new InvalidOperationException("Image not found");
        }

        await ClearMainForProductAsync(image.ProductId, cancellationToken);

        image.IsMain = true;
        image.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        return ToDto(image);
    }

    public async Task<ProductImageDto> AssignVariantAsync(Guid imageId, Guid? variantId, CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages.FindAsync(new object[] { imageId }, cancellationToken);
        if (image == null)
        {
            throw new InvalidOperationException("Image not found");
        }

        if (variantId.HasValue &&
            !await _context.ProductVariants.AnyAsync(v => v.Id == variantId.Value && v.ProductId == image.ProductId, cancellationToken))
        {
            throw new InvalidOperationException("Variant does not belong to this product");
        }

        image.ProductVariantId = variantId;
        image.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        return ToDto(image);
    }

    public async Task ReorderProductImagesAsync(Guid productId, IReadOnlyList<ImageOrderItem> items, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        var images = await _context.ProductImages
            .Where(i => i.ProductId == productId)
            .ToListAsync(cancellationToken);

        var imagesById = images.ToDictionary(i => i.Id);
        var orderedIds = items.OrderBy(i => i.DisplayOrder).Select(i => i.ImageId).ToList();

        for (var index = 0; index < orderedIds.Count; index++)
        {
            if (imagesById.TryGetValue(orderedIds[index], out var image))
            {
                image.DisplayOrder = index;
                image.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);
        _logger.LogInformation("Reordered {Count} images for product {ProductId}", orderedIds.Count, productId);
    }

    public async Task<ProductImageDto> ReplaceImageAsync(Guid imageId, UploadedFileInput file, CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages.FindAsync(new object[] { imageId }, cancellationToken);
        if (image == null)
        {
            throw new InvalidOperationException("Image not found");
        }

        var validation = await ValidateAsync(file, cancellationToken);

        var extension = GetExtension(validation.DetectedFormat!);
        var directory = FileStoragePath.GetDirectory(image.FileName);
        var newPath = FileStoragePath.Combine(directory, $"{Guid.NewGuid():N}{extension}");

        await _storage.SaveAsync(newPath, file.Content, validation.NormalizedContentType!, cancellationToken);

        await _processing.DeleteDerivativesAsync(image.FileName, cancellationToken);
        await _storage.DeleteAsync(image.FileName, cancellationToken);

        image.FileName = newPath;
        image.OriginalFileName = file.OriginalFileName;
        image.ImageFormat = validation.DetectedFormat!;
        image.ContentType = validation.NormalizedContentType!;
        image.SizeBytes = file.Length;
        image.Width = validation.Width;
        image.Height = validation.Height;
        image.ProcessingStatus = "Pending";
        image.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        await _queue.EnqueueAsync(new ImageProcessingJob(image.Id, newPath), cancellationToken);

        _logger.LogInformation("Replaced image {ImageId}", imageId);
        return ToDto(image);
    }

    public async Task<bool> DeleteImageAsync(Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages.FindAsync(new object[] { imageId }, cancellationToken);
        if (image == null)
        {
            return false;
        }

        var wasMain = image.IsMain;
        var productId = image.ProductId;

        _context.ProductImages.Remove(image);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        await _processing.DeleteDerivativesAsync(image.FileName, cancellationToken);
        await _storage.DeleteAsync(image.FileName, cancellationToken);

        if (wasMain)
        {
            var next = await _context.ProductImages
                .Where(i => i.ProductId == productId)
                .OrderBy(i => i.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken);

            if (next != null)
            {
                next.IsMain = true;
                next.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation("Deleted product image {ImageId}", imageId);
        return true;
    }

    public async Task<bool> DeleteCategoryImageAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories.FindAsync(new object[] { categoryId }, cancellationToken);
        if (category == null || string.IsNullOrWhiteSpace(category.ImageUrl))
        {
            return false;
        }

        await DeleteStoredFileByUrlAsync(category.ImageUrl, cancellationToken);
        category.ImageUrl = null;
        category.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteBrandImageAsync(Guid brandId, CancellationToken cancellationToken = default)
    {
        var brand = await _context.Brands.FindAsync(new object[] { brandId }, cancellationToken);
        if (brand == null || string.IsNullOrWhiteSpace(brand.LogoUrl))
        {
            return false;
        }

        await DeleteStoredFileByUrlAsync(brand.LogoUrl, cancellationToken);
        brand.LogoUrl = null;
        brand.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteCollectionImageAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var collection = await _context.Collections.FindAsync(new object[] { collectionId }, cancellationToken);
        if (collection == null || string.IsNullOrWhiteSpace(collection.BannerImageUrl))
        {
            return false;
        }

        await DeleteStoredFileByUrlAsync(collection.BannerImageUrl, cancellationToken);
        collection.BannerImageUrl = null;
        collection.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        return true;
    }

    public async Task<int> GetProductImageCountAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.ProductImages.CountAsync(i => i.ProductId == productId, cancellationToken);
    }

    private async Task<string> UploadReferencedImageAsync(
        string directory,
        UploadedFileInput file,
        Func<string, Task> apply,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(file, cancellationToken);

        var extension = GetExtension(validation.DetectedFormat!);
        var relativePath = FileStoragePath.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        await _storage.SaveAsync(relativePath, file.Content, validation.NormalizedContentType!, cancellationToken);

        var url = _storage.ResolveUrl(relativePath);
        await apply(url);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        await _queue.EnqueueAsync(new ImageProcessingJob(Guid.Empty, relativePath), cancellationToken);

        return url;
    }

    private async Task<ImageValidationResult> ValidateAsync(UploadedFileInput file, CancellationToken cancellationToken)
    {
        var validation = await _validation.ValidateAsync(file, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ImageValidationException(validation.Errors);
        }

        return validation;
    }

    private async Task ClearMainForProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var mains = await _context.ProductImages
            .Where(i => i.ProductId == productId && i.IsMain)
            .ToListAsync(cancellationToken);

        foreach (var main in mains)
        {
            main.IsMain = false;
        }
    }

    private async Task DeleteStoredFileByUrlAsync(string url, CancellationToken cancellationToken)
    {
        var baseUrl = _storage.ResolveUrl(string.Empty).TrimEnd('/');
        if (!url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relativePath = url[baseUrl.Length..].TrimStart('/');
        await _processing.DeleteDerivativesAsync(relativePath, cancellationToken);
        await _storage.DeleteAsync(relativePath, cancellationToken);
    }

    private ProductImageDto ToDto(ProductImage image)
    {
        var url = _storage.ResolveUrl(image.FileName);

        return new ProductImageDto(
            image.Id,
            image.ProductId,
            image.ProductVariantId,
            image.FileName,
            url,
            _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(image.FileName, ImageDerivatives.All[ImageDerivativeKind.Thumbnail])),
            _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(image.FileName, ImageDerivatives.All[ImageDerivativeKind.ProductCard])),
            _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(image.FileName, ImageDerivatives.All[ImageDerivativeKind.ProductDetail])),
            _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(image.FileName, ImageDerivatives.All[ImageDerivativeKind.Gallery])),
            image.AltText,
            image.Caption,
            image.IsMain,
            image.DisplayOrder,
            image.ImageFormat,
            image.ContentType,
            image.SizeBytes,
            image.Width,
            image.Height,
            image.ProcessingStatus,
            image.CreatedAtUtc
        );
    }

    private async Task InvalidateHomePageCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(FashionStore.Application.Common.CacheKeys.HomePage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate homepage cache after image change");
        }
    }

    private static string GetExtension(string format) => format.ToLowerInvariant() switch
    {
        "jpeg" => ".jpg",
        "png" => ".png",
        "webp" => ".webp",
        "avif" => ".avif",
        _ => $".{format}"
    };
}
