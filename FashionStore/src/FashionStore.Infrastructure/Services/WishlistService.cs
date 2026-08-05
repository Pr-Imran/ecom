using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Customer wishlist implementation. All data access is scoped to the supplied
/// customer id so one customer can never read or mutate another customer's list.
/// Display values are always recomputed from the catalogue on read; the client only
/// ever submits product and variant identifiers.
/// </summary>
public sealed class WishlistService : IWishlistService
{
    private const int RecentlyViewedCount = 8;
    private const string ColourAttributeName = "Colour";
    private const string SizeAttributeName = "Size";

    private readonly AppDbContext _context;
    private readonly IAddToCartService _addToCartService;
    private readonly ICatalogService _catalogService;
    private readonly IFileStorageService _storage;
    private readonly ILogger<WishlistService> _logger;

    public WishlistService(
        AppDbContext context,
        IAddToCartService addToCartService,
        ICatalogService catalogService,
        IFileStorageService storage,
        ILogger<WishlistService> logger)
    {
        _context = context;
        _addToCartService = addToCartService;
        _catalogService = catalogService;
        _storage = storage;
        _logger = logger;
    }

    public async Task<WishlistViewData> GetWishlistAsync(
        string userId,
        IReadOnlyList<Guid>? recentlyViewedIds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rows = await _context.WishlistItems
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Where(w => w.Product != null &&
                w.Product.IsActive &&
                w.Product.PublishedAtUtc != null &&
                w.Product.PublishedAtUtc <= now &&
                (w.ProductVariantId == null || w.Variant!.IsActive))
            .OrderByDescending(w => w.CreatedAtUtc)
            .Select(w => new
            {
                w.Id,
                w.ProductId,
                w.ProductVariantId,
                ProductName = w.Product != null ? w.Product.Name : null,
                Slug = w.Product != null ? w.Product.Slug : null,
                BrandName = w.Product != null && w.Product.Brand != null ? w.Product.Brand.Name : null,
                IsProductActive = w.Product != null &&
                    w.Product.IsActive &&
                    w.Product.PublishedAtUtc != null &&
                    w.Product.PublishedAtUtc <= now,
                ImageFileName = w.Product != null
                    ? w.Product.Images
                        .OrderBy(i => i.IsMain ? 0 : 1)
                        .ThenBy(i => i.DisplayOrder)
                        .Select(i => i.FileName)
                        .FirstOrDefault()
                    : null,
                ImageAltText = w.Product != null
                    ? w.Product.Images
                        .OrderBy(i => i.IsMain ? 0 : 1)
                        .ThenBy(i => i.DisplayOrder)
                        .Select(i => i.AltText)
                        .FirstOrDefault()
                    : null,
                HasVariations = w.Product != null && w.Product.Variants.Any(v => v.IsActive),
                VariantSku = w.Variant != null ? w.Variant.Sku : null,
                VariantPrice = w.Variant != null ? (decimal?)w.Variant.Price : null,
                VariantCompareAtPrice = w.Variant != null ? w.Variant.CompareAtPrice : null,
                VariantImageUrl = w.Variant != null ? w.Variant.ImageUrl : null,
                VariantIsActive = w.Variant == null || w.Variant.IsActive,
                VariantAvailableStock = w.Variant != null
                    ? Math.Max(0, (w.Variant.StockQuantity ?? 0) - (w.Variant.ReservedStock ?? 0))
                    : 0,
                ColourName = w.Variant != null && w.Variant.VariantAttributeValues.Any(vav =>
                    vav.AttributeValue != null &&
                    vav.AttributeValue.ProductAttribute != null &&
                    vav.AttributeValue.ProductAttribute.Name == ColourAttributeName)
                    ? w.Variant.VariantAttributeValues
                        .Where(vav => vav.AttributeValue != null &&
                            vav.AttributeValue.ProductAttribute != null &&
                            vav.AttributeValue.ProductAttribute.Name == ColourAttributeName)
                        .Select(vav => vav.AttributeValue!.Name)
                        .FirstOrDefault()
                    : null,
                SizeName = w.Variant != null && w.Variant.VariantAttributeValues.Any(vav =>
                    vav.AttributeValue != null &&
                    vav.AttributeValue.ProductAttribute != null &&
                    vav.AttributeValue.ProductAttribute.Name == SizeAttributeName)
                    ? w.Variant.VariantAttributeValues
                        .Where(vav => vav.AttributeValue != null &&
                            vav.AttributeValue.ProductAttribute != null &&
                            vav.AttributeValue.ProductAttribute.Name == SizeAttributeName)
                        .Select(vav => vav.AttributeValue!.Name)
                        .FirstOrDefault()
                    : null
            })
            .ToListAsync(cancellationToken);

        var items = new List<WishlistItemDto>();
        var productIds = rows.Where(r => !string.IsNullOrEmpty(r.ProductName)).Select(r => r.ProductId).Distinct().ToList();
        var basePrices = productIds.Count > 0
            ? await _context.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.BasePrice, p.CompareAtPrice })
                .ToDictionaryAsync(p => p.Id, p => (p.BasePrice, p.CompareAtPrice), cancellationToken)
            : new Dictionary<Guid, (decimal BasePrice, decimal? CompareAtPrice)>();

        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.ProductName) || string.IsNullOrEmpty(row.Slug))
            {
                continue;
            }

            var isInStock = row.VariantIsActive && (row.VariantAvailableStock > 0 || row.ProductVariantId == null);
            var baseRow = basePrices.TryGetValue(row.ProductId, out var b) ? b : (0m, (decimal?)null);
            var price = row.VariantPrice ?? baseRow.Item1;
            var compareAtPrice = row.VariantCompareAtPrice ?? baseRow.Item2;
            var imageFileName = row.VariantImageUrl ?? row.ImageFileName;

            items.Add(new WishlistItemDto(
                row.Id,
                row.ProductId,
                row.ProductVariantId,
                row.ProductName,
                row.Slug,
                row.BrandName,
                CatalogQueryHelpers.ResolveImageUrl(_storage, imageFileName),
                CatalogQueryHelpers.ResolveCardImageUrl(_storage, imageFileName),
                row.ImageAltText,
                price,
                compareAtPrice,
                CatalogQueryHelpers.CalculateDiscountPercent(price, compareAtPrice),
                row.VariantSku,
                row.ColourName,
                row.SizeName,
                row.IsProductActive && isInStock,
                row.VariantAvailableStock,
                row.IsProductActive,
                row.HasVariations && row.ProductVariantId == null));
        }

        var recentlyViewed = await LoadRecentlyViewedAsync(recentlyViewedIds, cancellationToken);

        return new WishlistViewData(items, items.Count, true, recentlyViewed);
    }

    public async Task<WishlistMutationResult> AddAsync(
        string userId,
        Guid productId,
        Guid? variantId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null || !product.IsActive || product.PublishedAtUtc == null || product.PublishedAtUtc > now)
        {
            return Fail(0, "This product is no longer available.");
        }

        if (variantId.HasValue)
        {
            var variant = await _context.ProductVariants
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == variantId.Value, cancellationToken);

            if (variant is null || variant.ProductId != productId || !variant.IsActive)
            {
                return Fail(0, "The selected variation is no longer available.");
            }
        }

        var existing = await _context.WishlistItems
            .AnyAsync(
                w => w.UserId == userId &&
                     w.ProductId == productId &&
                     w.ProductVariantId == variantId,
                cancellationToken);

        if (existing)
        {
            var count = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
            _logger.LogInformation("Wishlist duplicate ignored for user {UserId} product {ProductId}", userId, productId);
            return new WishlistMutationResult(true, null, count);
        }

        _context.WishlistItems.Add(new WishlistItem
        {
            UserId = userId,
            ProductId = productId,
            ProductVariantId = variantId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        var total = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
        _logger.LogInformation("Added product {ProductId} to wishlist for user {UserId}", productId, userId);

        return new WishlistMutationResult(true, null, total);
    }

    public async Task<WishlistMutationResult> RemoveAsync(
        string userId,
        Guid wishlistItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == wishlistItemId && w.UserId == userId, cancellationToken);

        if (item is null)
        {
            var count = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
            return new WishlistMutationResult(false, "Wishlist item not found.", count);
        }

        _context.WishlistItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);

        var total = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
        _logger.LogInformation("Removed wishlist item {WishlistItemId} for user {UserId}", wishlistItemId, userId);

        return new WishlistMutationResult(true, null, total);
    }

    public async Task<WishlistMutationResult> RemoveByProductAsync(
        string userId,
        Guid productId,
        Guid? variantId,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.WishlistItems
            .FirstOrDefaultAsync(
                w => w.UserId == userId &&
                     w.ProductId == productId &&
                     w.ProductVariantId == variantId,
                cancellationToken);

        if (item is null)
        {
            var count = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
            return new WishlistMutationResult(false, "Wishlist item not found.", count);
        }

        _context.WishlistItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);

        var total = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
        _logger.LogInformation("Removed product {ProductId} from wishlist for user {UserId}", productId, userId);

        return new WishlistMutationResult(true, null, total);
    }

    public async Task<int> GetCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
    }

    public async Task<WishlistMutationResult> MoveToCartAsync(
        string userId,
        Guid wishlistItemId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.WishlistItems
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == wishlistItemId && w.UserId == userId, cancellationToken);

        if (item is null)
        {
            var count = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
            return new WishlistMutationResult(false, "Wishlist item not found.", count);
        }

        var variantId = item.ProductVariantId;

        if (variantId is null)
        {
            variantId = await _context.ProductVariants
                .AsNoTracking()
                .Where(v => v.ProductId == item.ProductId && v.IsActive)
                .OrderByDescending(v => v.IsDefault)
                .ThenBy(v => v.CreatedAtUtc)
                .Select(v => (Guid?)v.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (!variantId.HasValue)
        {
            var count = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
            return new WishlistMutationResult(false, "This product has no available variation to add to cart.", count);
        }

        var validation = await _addToCartService.ValidateAsync(
            new AddToCartRequest(item.ProductId, variantId.Value, quantity),
            cancellationToken);

        if (!validation.Success)
        {
            var count = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
            return new WishlistMutationResult(false, validation.ErrorMessage, count);
        }

        var entry = await _context.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == wishlistItemId && w.UserId == userId, cancellationToken);

        if (entry is not null)
        {
            _context.WishlistItems.Remove(entry);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var total = await _context.WishlistItems.CountAsync(w => w.UserId == userId, cancellationToken);
        _logger.LogInformation("Moved wishlist item {WishlistItemId} to cart for user {UserId}", wishlistItemId, userId);

        return new WishlistMutationResult(true, null, total);
    }

    public async Task<WishlistViewData> ResolveAnonymousAsync(
        IReadOnlyList<WishlistMutationRequest> anonymousEntries,
        IReadOnlyList<Guid>? recentlyViewedIds,
        CancellationToken cancellationToken = default)
    {
        if (anonymousEntries.Count == 0)
        {
            var emptyRecentlyViewed = await LoadRecentlyViewedAsync(recentlyViewedIds, cancellationToken);
            return new WishlistViewData(Array.Empty<WishlistItemDto>(), 0, false, emptyRecentlyViewed);
        }

        var now = DateTime.UtcNow;
        var unique = anonymousEntries.DistinctBy(e => (e.ProductId, e.VariantId)).ToList();
        var productIds = unique.Select(e => e.ProductId).Distinct().ToList();

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                IsProductActive = p.IsActive && p.PublishedAtUtc != null && p.PublishedAtUtc <= now,
                ImageFileName = p.Images
                    .OrderBy(i => i.IsMain ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.FileName)
                    .FirstOrDefault(),
                ImageAltText = p.Images
                    .OrderBy(i => i.IsMain ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.AltText)
                    .FirstOrDefault(),
                HasVariations = p.Variants.Any(v => v.IsActive)
            })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var variantIds = unique.Where(e => e.VariantId.HasValue).Select(e => e.VariantId!.Value).Distinct().ToList();
        var variants = variantIds.Count > 0
            ? await _context.ProductVariants
                .AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .Select(v => new VariantRow(
                    v.Id,
                    v.ProductId,
                    v.Sku,
                    v.Price,
                    v.CompareAtPrice,
                    v.IsActive,
                    v.ImageUrl,
                    Math.Max(0, (v.StockQuantity ?? 0) - (v.ReservedStock ?? 0)),
                    v.VariantAttributeValues
                        .Where(vav => vav.AttributeValue != null &&
                            vav.AttributeValue.ProductAttribute != null &&
                            vav.AttributeValue.ProductAttribute.Name == ColourAttributeName)
                        .Select(vav => vav.AttributeValue!.Name)
                        .FirstOrDefault(),
                    v.VariantAttributeValues
                        .Where(vav => vav.AttributeValue != null &&
                            vav.AttributeValue.ProductAttribute != null &&
                            vav.AttributeValue.ProductAttribute.Name == SizeAttributeName)
                        .Select(vav => vav.AttributeValue!.Name)
                        .FirstOrDefault()))
                .ToDictionaryAsync(v => v.Id, cancellationToken)
            : new Dictionary<Guid, VariantRow>();

        var items = new List<WishlistItemDto>();
        var basePrices = productIds.Count > 0
            ? await _context.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.BasePrice, p.CompareAtPrice })
                .ToDictionaryAsync(p => p.Id, p => (p.BasePrice, p.CompareAtPrice), cancellationToken)
            : new Dictionary<Guid, (decimal BasePrice, decimal? CompareAtPrice)>();
        foreach (var entry in unique)
        {
            if (!products.TryGetValue(entry.ProductId, out var product) || !product.IsProductActive)
            {
                continue;
            }

            var isInStock = true;
            var availableStock = 0;
            string? sku = null;
            string? colourName = null;
            string? sizeName = null;
            decimal price = 0m;
            decimal? compareAtPrice = null;
            string? imageFileName = product.ImageFileName;
            var baseRow = basePrices.TryGetValue(entry.ProductId, out var b) ? b : (0m, (decimal?)null);

            if (entry.VariantId.HasValue && variants.TryGetValue(entry.VariantId.Value, out var variantRow))
            {
                isInStock = variantRow.IsActive && variantRow.AvailableStock > 0;
                availableStock = variantRow.AvailableStock;
                sku = variantRow.Sku;
                colourName = variantRow.ColourName;
                sizeName = variantRow.SizeName;
                price = variantRow.Price;
                compareAtPrice = variantRow.CompareAtPrice;
                imageFileName = variantRow.ImageUrl ?? product.ImageFileName;
            }
            else if (entry.VariantId is null)
            {
                price = baseRow.Item1;
                compareAtPrice = baseRow.Item2;
            }
            else
            {
                continue;
            }

            items.Add(new WishlistItemDto(
                Guid.Empty,
                product.Id,
                entry.VariantId,
                product.Name,
                product.Slug,
                product.BrandName,
                CatalogQueryHelpers.ResolveImageUrl(_storage, imageFileName),
                CatalogQueryHelpers.ResolveCardImageUrl(_storage, imageFileName),
                product.ImageAltText,
                price,
                compareAtPrice,
                CatalogQueryHelpers.CalculateDiscountPercent(price, compareAtPrice),
                sku,
                colourName,
                sizeName,
                isInStock,
                availableStock,
                true,
                product.HasVariations && entry.VariantId == null));
        }

        var recentlyViewed = await LoadRecentlyViewedAsync(recentlyViewedIds, cancellationToken);
        return new WishlistViewData(items, items.Count, false, recentlyViewed);
    }

    private sealed record VariantRow(
        Guid Id,
        Guid ProductId,
        string Sku,
        decimal Price,
        decimal? CompareAtPrice,
        bool IsActive,
        string? ImageUrl,
        int AvailableStock,
        string? ColourName,
        string? SizeName);

    public async Task<int> MergeAsync(
        string userId,
        IReadOnlyList<WishlistMutationRequest> anonymousEntries,
        CancellationToken cancellationToken = default)
    {
        if (anonymousEntries.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var added = 0;

        var validProductIds = (await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.PublishedAtUtc != null && p.PublishedAtUtc <= now)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var entry in anonymousEntries.DistinctBy(e => (e.ProductId, e.VariantId)))
        {
            if (!validProductIds.Contains(entry.ProductId))
            {
                continue;
            }

            if (entry.VariantId.HasValue)
            {
                var validVariant = await _context.ProductVariants
                    .AsNoTracking()
                    .AnyAsync(
                        v => v.Id == entry.VariantId.Value &&
                             v.ProductId == entry.ProductId &&
                             v.IsActive,
                        cancellationToken);

                if (!validVariant)
                {
                    continue;
                }
            }

            var exists = await _context.WishlistItems
                .AnyAsync(
                    w => w.UserId == userId &&
                         w.ProductId == entry.ProductId &&
                         w.ProductVariantId == entry.VariantId,
                    cancellationToken);

            if (exists)
            {
                continue;
            }

            _context.WishlistItems.Add(new WishlistItem
            {
                UserId = userId,
                ProductId = entry.ProductId,
                ProductVariantId = entry.VariantId,
                CreatedAtUtc = DateTime.UtcNow
            });
            added++;
        }

        if (added > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Merged {Count} anonymous wishlist entries into wishlist for user {UserId}", added, userId);
        }

        return added;
    }

    private async Task<IReadOnlyList<ProductListItemDto>> LoadRecentlyViewedAsync(
        IReadOnlyList<Guid>? recentlyViewedIds,
        CancellationToken cancellationToken)
    {
        var ids = (recentlyViewedIds ?? Array.Empty<Guid>())
            .Distinct()
            .Take(RecentlyViewedCount)
            .ToList();

        return ids.Count > 0
            ? await _catalogService.GetProductCardsByIdsAsync(ids, cancellationToken)
            : Array.Empty<ProductListItemDto>();
    }

    private static WishlistMutationResult Fail(int itemCount, string message)
    {
        return new WishlistMutationResult(false, message, itemCount);
    }
}
