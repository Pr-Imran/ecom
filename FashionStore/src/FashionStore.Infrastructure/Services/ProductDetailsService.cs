using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Composes the storefront product details page from the catalogue, image gallery,
/// storefront variations, related products and the recently-viewed rail. All values
/// are server-computed and read-only; nothing from the browser is trusted.
/// </summary>
public sealed class ProductDetailsService : IProductDetailsService
{
    private const int RelatedProductsCount = 8;
    private const int RecentlyViewedCount = 8;

    private readonly AppDbContext _context;
    private readonly IProductVariationService _variationService;
    private readonly ICatalogService _catalogService;
    private readonly IFileStorageService _storage;
    private readonly ILogger<ProductDetailsService> _logger;

    public ProductDetailsService(
        AppDbContext context,
        IProductVariationService variationService,
        ICatalogService catalogService,
        IFileStorageService storage,
        ILogger<ProductDetailsService> logger)
    {
        _context = context;
        _variationService = variationService;
        _catalogService = catalogService;
        _storage = storage;
        _logger = logger;
    }

    public async Task<ProductDetailsData?> GetDetailsAsync(
        string slug,
        IReadOnlyList<Guid>? recentlyViewedIds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug && p.IsActive && p.PublishedAtUtc != null && p.PublishedAtUtc <= now)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.FullDescription,
                p.Material,
                p.Fabric,
                p.CareInstructions,
                p.Gender,
                p.CountryOfOrigin,
                p.BaseSku,
                p.BasePrice,
                p.CompareAtPrice,
                p.AllowReviews,
                p.SeoTitle,
                p.SeoDescription,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                CategoryName = p.Category != null ? p.Category.Name : null,
                CategorySlug = p.Category != null ? p.Category.Slug : null,
                CollectionName = p.Collection != null ? p.Collection.Name : null,
                CollectionSlug = p.Collection != null ? p.Collection.Slug : null,
                p.CategoryId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var images = await _context.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == product.Id)
            .OrderBy(i => i.IsMain ? 0 : 1)
            .ThenBy(i => i.DisplayOrder)
            .Select(i => new
            {
                i.Id,
                i.FileName,
                i.AltText,
                i.Caption,
                i.IsMain,
                i.DisplayOrder
            })
            .ToListAsync(cancellationToken);

        var imageDtos = images
            .Select(i => new ProductDetailsImageDto(
                i.Id,
                _storage.ResolveUrl(i.FileName),
                _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(i.FileName, ImageDerivatives.All[ImageDerivativeKind.Thumbnail])),
                _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(i.FileName, ImageDerivatives.All[ImageDerivativeKind.ProductCard])),
                _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(i.FileName, ImageDerivatives.All[ImageDerivativeKind.ProductDetail])),
                _storage.ResolveUrl(ImageDerivatives.BuildDerivativePath(i.FileName, ImageDerivatives.All[ImageDerivativeKind.Gallery])),
                i.AltText,
                i.Caption,
                i.IsMain,
                i.DisplayOrder))
            .ToList();

        var variations = await _variationService.GetStorefrontVariationsAsync(product.Id, cancellationToken);

        var defaultVariant = variations.Variants.FirstOrDefault(v => v.IsDefault) ?? variations.Variants.FirstOrDefault();
        var defaultVariantAttributeValueIds = defaultVariant?.AttributeValueIds.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Guid.Parse(value!))
            .ToList();

        var (averageRating, reviewCount) = await GetRatingAsync(product.Id, cancellationToken);

        var relatedIds = await GetRelatedIdsAsync(product.Id, product.CategoryId, now, cancellationToken);
        var relatedProducts = await _catalogService.GetProductCardsByIdsAsync(relatedIds, cancellationToken);

        var recentlyViewedIdsExcludingSelf = (recentlyViewedIds ?? Array.Empty<Guid>())
            .Where(id => id != product.Id)
            .Distinct()
            .Take(RecentlyViewedCount)
            .ToList();

        var recentlyViewed = await _catalogService.GetProductCardsByIdsAsync(recentlyViewedIdsExcludingSelf, cancellationToken);

        var price = defaultVariant?.Price ?? product.BasePrice;
        var compareAtPrice = defaultVariant?.CompareAtPrice ?? product.CompareAtPrice;
        var availableStock = variations.Variants.Sum(v => v.StockQuantity ?? 0);

        return new ProductDetailsData(
            product.Id,
            product.Name,
            product.Slug,
            product.ShortDescription,
            product.FullDescription,
            product.BrandName,
            product.CategoryName,
            product.CategorySlug,
            product.CollectionName,
            product.CollectionSlug,
            product.Material,
            product.Fabric,
            product.CareInstructions,
            product.Gender,
            product.CountryOfOrigin,
            product.BaseSku,
            price,
            compareAtPrice,
            CatalogQueryHelpers.CalculateDiscountPercent(price, compareAtPrice),
            product.AllowReviews,
            averageRating,
            reviewCount,
            availableStock > 0,
            availableStock,
            HasVideo: false,
            VideoUrl: null,
            product.SeoTitle,
            product.SeoDescription,
            imageDtos,
            variations,
            defaultVariantAttributeValueIds,
            relatedProducts,
            recentlyViewed);
    }

    private async Task<(double Average, int Count)> GetRatingAsync(Guid productId, CancellationToken cancellationToken)
    {
        var row = await _context.ProductReviews
            .AsNoTracking()
            .Where(r => r.IsApproved && r.ProductId == productId)
            .GroupBy(r => r.ProductId)
            .Select(g => new { Average = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? (0, 0) : (row.Average, row.Count);
    }

    private async Task<List<Guid>> GetRelatedIdsAsync(
        Guid productId,
        Guid categoryId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var relatedIds = await _context.RelatedProducts
            .AsNoTracking()
            .Where(r => r.ProductId == productId)
            .OrderBy(r => r.DisplayOrder)
            .Select(r => r.RelatedProductId)
            .ToListAsync(cancellationToken);

        if (relatedIds.Count < RelatedProductsCount)
        {
            var fillCount = RelatedProductsCount - relatedIds.Count;
            var fill = await _context.Products
                .AsNoTracking()
                .Where(p =>
                    p.Id != productId &&
                    !relatedIds.Contains(p.Id) &&
                    p.IsActive &&
                    p.PublishedAtUtc != null &&
                    p.PublishedAtUtc <= now)
                .Where(p => p.CategoryId == categoryId)
                .OrderByDescending(p => p.IsFeatured)
                .ThenByDescending(p => p.CreatedAtUtc)
                .Take(fillCount)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            relatedIds.AddRange(fill);
        }

        return relatedIds.Take(RelatedProductsCount).ToList();
    }
}
