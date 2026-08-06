using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Customer shopping cart implementation. All data access is scoped to the
/// supplied customer id so one customer can never read or mutate another
/// customer's cart. Every read and mutation re-verifies the product and variant
/// active state, current price, available stock and the maximum quantity, and all
/// display values are recomputed from the catalogue on read.
/// </summary>
public sealed class CartService : ICartService
{
    private const int MaxQuantity = 99;
    private const int CartExpirationDays = 30;
    private const string ColourAttributeName = "Colour";
    private const string SizeAttributeName = "Size";

    private readonly AppDbContext _context;
    private readonly IAddToCartService _addToCartService;
    private readonly IFileStorageService _storage;
    private readonly ILogger<CartService> _logger;

    public CartService(
        AppDbContext context,
        IAddToCartService addToCartService,
        IFileStorageService storage,
        ILogger<CartService> logger)
    {
        _context = context;
        _addToCartService = addToCartService;
        _storage = storage;
        _logger = logger;
    }

    public async Task<CartViewData> GetCartAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await PurgeExpiredAsync(userId, now, cancellationToken);

        var rows = await LoadRowsAsync(
            q => q.Where(c => c.UserId == userId),
            now,
            cancellationToken);

        var items = rows.Select(BuildItem).Where(i => i != null).Cast<CartItemDto>().ToList();
        return BuildView(items, true);
    }

    public async Task<CartViewData> ResolveAnonymousAsync(
        IReadOnlyList<AnonymousCartEntry> anonymousEntries,
        CancellationToken cancellationToken = default)
    {
        if (anonymousEntries.Count == 0)
        {
            return BuildView(new List<CartItemDto>(), false);
        }

        var now = DateTime.UtcNow;
        var unique = anonymousEntries
            .DistinctBy(e => (e.ProductId, e.VariantId))
            .ToList();

        var productIds = unique.Select(e => e.ProductId).Distinct().ToList();
        var variantIds = unique.Select(e => e.VariantId).Distinct().ToList();

        var variants = variantIds.Count > 0
            ? await _context.ProductVariants
                .AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .Select(v => new VariantRow(
                    v.Id,
                    v.ProductId,
                    v.Product != null ? v.Product.Name : null,
                    v.Product != null ? v.Product.Slug : null,
                    v.Product != null && v.Product.Brand != null ? v.Product.Brand.Name : null,
                    v.Product != null && v.Product.IsActive && v.Product.PublishedAtUtc != null && v.Product.PublishedAtUtc <= now,
                    v.Product != null ? v.Product.Images
                        .OrderBy(i => i.IsMain ? 0 : 1)
                        .ThenBy(i => i.DisplayOrder)
                        .Select(i => i.FileName)
                        .FirstOrDefault() : null,
                    v.Product != null ? v.Product.Images
                        .OrderBy(i => i.IsMain ? 0 : 1)
                        .ThenBy(i => i.DisplayOrder)
                        .Select(i => i.AltText)
                        .FirstOrDefault() : null,
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

        var items = new List<CartItemDto>();
        foreach (var entry in unique)
        {
            if (!variants.TryGetValue(entry.VariantId, out var variant) ||
                variant.ProductId != entry.ProductId)
            {
                continue;
            }

            var item = BuildAnonymousItem(entry, variant);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return BuildView(items, false);
    }

    public async Task<CartMutationResult> AddAsync(
        string userId,
        Guid productId,
        Guid variantId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity < 1)
        {
            return Fail("Quantity must be at least 1.");
        }

        var validation = await _addToCartService.ValidateAsync(
            new AddToCartRequest(productId, variantId, quantity),
            cancellationToken);

        if (!validation.Success)
        {
            return Fail(validation.ErrorMessage ?? "This item could not be added to your cart.");
        }

        var existing = await _context.CartItems
            .FirstOrDefaultAsync(
                c => c.UserId == userId &&
                     c.ProductId == productId &&
                     c.ProductVariantId == variantId,
                cancellationToken);

        if (existing is not null)
        {
            var combined = existing.Quantity + quantity;
            if (combined > MaxQuantity)
            {
                return Fail($"You cannot add more than {MaxQuantity} of an item to your cart.");
            }

            if (validation.Item != null && combined > validation.Item.AvailableStock)
            {
                return Fail($"Only {validation.Item.AvailableStock} item(s) left in stock.");
            }

            existing.Quantity = combined;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _context.CartItems.Add(new CartItem
            {
                UserId = userId,
                ProductId = productId,
                ProductVariantId = variantId,
                Quantity = quantity,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var count = await GetCountAsync(userId, cancellationToken);
        _logger.LogInformation(
            "Added variant {VariantId} of product {ProductId} (quantity {Quantity}) to cart for user {UserId}",
            variantId,
            productId,
            quantity,
            userId);

        return new CartMutationResult(true, null, count);
    }

    public async Task<CartMutationResult> UpdateQuantityAsync(
        string userId,
        Guid productId,
        Guid variantId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity < 1)
        {
            return Fail("Quantity must be at least 1.");
        }

        if (quantity > MaxQuantity)
        {
            return Fail($"Quantity cannot exceed {MaxQuantity}.");
        }

        var item = await _context.CartItems
            .FirstOrDefaultAsync(
                c => c.UserId == userId &&
                     c.ProductId == productId &&
                     c.ProductVariantId == variantId,
                cancellationToken);

        if (item is null)
        {
            return Fail("This item is no longer in your cart.");
        }

        var validation = await _addToCartService.ValidateAsync(
            new AddToCartRequest(productId, variantId, quantity),
            cancellationToken);

        if (!validation.Success)
        {
            return Fail(validation.ErrorMessage ?? "This quantity is not available.");
        }

        item.Quantity = quantity;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var count = await GetCountAsync(userId, cancellationToken);
        _logger.LogInformation(
            "Updated quantity to {Quantity} for variant {VariantId} of product {ProductId} in cart for user {UserId}",
            quantity,
            variantId,
            productId,
            userId);

        return new CartMutationResult(true, null, count, validation.Item is null ? null : BuildItemFromValidation(validation.Item));
    }

    public async Task<CartMutationResult> RemoveAsync(
        string userId,
        Guid productId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.CartItems
            .FirstOrDefaultAsync(
                c => c.UserId == userId &&
                     c.ProductId == productId &&
                     c.ProductVariantId == variantId,
                cancellationToken);

        if (item is not null)
        {
            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var count = await GetCountAsync(userId, cancellationToken);
        _logger.LogInformation(
            "Removed variant {VariantId} of product {ProductId} from cart for user {UserId}",
            variantId,
            productId,
            userId);

        return new CartMutationResult(true, null, count);
    }

    public async Task<CartMutationResult> ClearAsync(string userId, CancellationToken cancellationToken = default)
    {
        await _context.CartItems
            .Where(c => c.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Cleared cart for user {UserId}", userId);
        return new CartMutationResult(true, null, 0);
    }

    public async Task<int> GetCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await PurgeExpiredAsync(userId, now, cancellationToken);

        return await _context.CartItems
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .SumAsync(c => (int?)c.Quantity, cancellationToken) ?? 0;
    }

    public async Task<int> MergeAsync(
        string userId,
        IReadOnlyList<AnonymousCartEntry> anonymousEntries,
        CancellationToken cancellationToken = default)
    {
        if (anonymousEntries.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var merged = 0;

        foreach (var entry in anonymousEntries.DistinctBy(e => (e.ProductId, e.VariantId)))
        {
            var validation = await _addToCartService.ValidateAsync(
                new AddToCartRequest(entry.ProductId, entry.VariantId, entry.Quantity),
                cancellationToken);

            if (!validation.Success)
            {
                _logger.LogInformation(
                    "Skipping unavailable anonymous cart entry for product {ProductId} variant {VariantId}: {Reason}",
                    entry.ProductId,
                    entry.VariantId,
                    validation.ErrorMessage);
                continue;
            }

            var availableStock = validation.Item?.AvailableStock ?? 0;
            var targetQuantity = Math.Min(Math.Min(entry.Quantity, availableStock), MaxQuantity);
            if (targetQuantity < 1)
            {
                continue;
            }

            var existing = await _context.CartItems
                .FirstOrDefaultAsync(
                    c => c.UserId == userId &&
                         c.ProductId == entry.ProductId &&
                         c.ProductVariantId == entry.VariantId,
                    cancellationToken);

            if (existing is not null)
            {
                var combined = Math.Min(Math.Min(existing.Quantity + targetQuantity, availableStock), MaxQuantity);
                existing.Quantity = Math.Max(1, combined);
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _context.CartItems.Add(new CartItem
                {
                    UserId = userId,
                    ProductId = entry.ProductId,
                    ProductVariantId = entry.VariantId,
                    Quantity = targetQuantity,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            merged++;
        }

        if (merged > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Merged {Count} anonymous cart entries into cart for user {UserId}", merged, userId);
        }

        return merged;
    }

    private async Task PurgeExpiredAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-CartExpirationDays);
        var expired = await _context.CartItems
            .Where(c => c.UserId == userId && c.UpdatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (expired.Count > 0)
        {
            _context.CartItems.RemoveRange(expired);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Purged {Count} expired cart lines for user {UserId}", expired.Count, userId);
        }
    }

    private sealed record CartRow(
        Guid Id,
        Guid ProductId,
        Guid ProductVariantId,
        string? ProductName,
        string? Slug,
        string? BrandName,
        bool IsProductActive,
        string? ImageFileName,
        string? ImageAltText,
        string Sku,
        decimal Price,
        decimal? CompareAtPrice,
        bool VariantIsActive,
        string? VariantImageUrl,
        int AvailableStock,
        string? ColourName,
        string? SizeName,
        int Quantity);

    private async Task<IReadOnlyList<CartRow>> LoadRowsAsync(
        Func<IQueryable<CartItem>, IQueryable<CartItem>> filter,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return await filter(_context.CartItems.AsNoTracking())
            .Select(c => new CartRow(
                c.Id,
                c.ProductId,
                c.ProductVariantId,
                c.Product != null ? c.Product.Name : null,
                c.Product != null ? c.Product.Slug : null,
                c.Product != null && c.Product.Brand != null ? c.Product.Brand.Name : null,
                c.Product != null &&
                    c.Product.IsActive &&
                    c.Product.PublishedAtUtc != null &&
                    c.Product.PublishedAtUtc <= now,
                c.Product != null ? c.Product.Images
                    .OrderBy(i => i.IsMain ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.FileName)
                    .FirstOrDefault() : null,
                c.Product != null ? c.Product.Images
                    .OrderBy(i => i.IsMain ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.AltText)
                    .FirstOrDefault() : null,
                c.Variant != null ? c.Variant.Sku : string.Empty,
                c.Variant != null ? c.Variant.Price : 0m,
                c.Variant != null ? c.Variant.CompareAtPrice : null,
                c.Variant == null || c.Variant.IsActive,
                c.Variant != null ? c.Variant.ImageUrl : null,
                c.Variant != null
                    ? Math.Max(0, (c.Variant.StockQuantity ?? 0) - (c.Variant.ReservedStock ?? 0))
                    : 0,
                c.Variant != null && c.Variant.VariantAttributeValues.Any(vav =>
                    vav.AttributeValue != null &&
                    vav.AttributeValue.ProductAttribute != null &&
                    vav.AttributeValue.ProductAttribute.Name == ColourAttributeName)
                    ? c.Variant.VariantAttributeValues
                        .Where(vav => vav.AttributeValue != null &&
                            vav.AttributeValue.ProductAttribute != null &&
                            vav.AttributeValue.ProductAttribute.Name == ColourAttributeName)
                        .Select(vav => vav.AttributeValue!.Name)
                        .FirstOrDefault()
                    : null,
                c.Variant != null && c.Variant.VariantAttributeValues.Any(vav =>
                    vav.AttributeValue != null &&
                    vav.AttributeValue.ProductAttribute != null &&
                    vav.AttributeValue.ProductAttribute.Name == SizeAttributeName)
                    ? c.Variant.VariantAttributeValues
                        .Where(vav => vav.AttributeValue != null &&
                            vav.AttributeValue.ProductAttribute != null &&
                            vav.AttributeValue.ProductAttribute.Name == SizeAttributeName)
                        .Select(vav => vav.AttributeValue!.Name)
                        .FirstOrDefault()
                    : null,
                c.Quantity))
            .ToListAsync(cancellationToken);
    }

    private sealed record VariantRow(
        Guid Id,
        Guid ProductId,
        string? ProductName,
        string? Slug,
        string? BrandName,
        bool IsProductActive,
        string? ImageFileName,
        string? ImageAltText,
        string Sku,
        decimal Price,
        decimal? CompareAtPrice,
        bool IsActive,
        string? ImageUrl,
        int AvailableStock,
        string? ColourName,
        string? SizeName);

    private CartItemDto? BuildItem(CartRow row)
    {
        if (string.IsNullOrEmpty(row.ProductName) || string.IsNullOrEmpty(row.Slug) || string.IsNullOrEmpty(row.Sku))
        {
            return null;
        }

        var isProductActive = row.IsProductActive;
        var isVariantActive = row.VariantIsActive;
        var isInStock = row.AvailableStock > 0;
        var quantityExceedsStock = row.Quantity > row.AvailableStock;
        var isAvailable = isProductActive && isVariantActive && isInStock && !quantityExceedsStock;
        var imageFileName = row.VariantImageUrl ?? row.ImageFileName;

        var unavailableReason = !isProductActive
            ? "This product is no longer available."
            : !isVariantActive
                ? "This variation is currently unavailable."
                : !isInStock
                    ? "This item is out of stock."
                    : quantityExceedsStock
                        ? $"Only {row.AvailableStock} item(s) left in stock."
                        : null;

        return new CartItemDto(
            row.Id,
            row.ProductId,
            row.ProductVariantId,
            row.ProductName,
            row.Slug,
            row.BrandName,
            CatalogQueryHelpers.ResolveImageUrl(_storage, imageFileName),
            CatalogQueryHelpers.ResolveCardImageUrl(_storage, imageFileName),
            row.ImageAltText,
            row.Sku,
            row.ColourName,
            row.SizeName,
            row.Price,
            row.CompareAtPrice,
            CatalogQueryHelpers.CalculateDiscountPercent(row.Price, row.CompareAtPrice),
            row.Quantity,
            row.Price * row.Quantity,
            row.AvailableStock,
            isAvailable,
            isInStock,
            isProductActive && isVariantActive,
            unavailableReason);
    }

    private CartItemDto? BuildAnonymousItem(AnonymousCartEntry entry, VariantRow variant)
    {
        if (string.IsNullOrEmpty(variant.ProductName) || string.IsNullOrEmpty(variant.Slug) || string.IsNullOrEmpty(variant.Sku))
        {
            return null;
        }

        var isProductActive = variant.IsProductActive;
        var isVariantActive = variant.IsActive;
        var isInStock = variant.AvailableStock > 0;
        var quantityExceedsStock = entry.Quantity > variant.AvailableStock;
        var isAvailable = isProductActive && isVariantActive && isInStock && !quantityExceedsStock;
        var imageFileName = variant.ImageUrl ?? variant.ImageFileName;

        var unavailableReason = !isProductActive
            ? "This product is no longer available."
            : !isVariantActive
                ? "This variation is currently unavailable."
                : !isInStock
                    ? "This item is out of stock."
                    : quantityExceedsStock
                        ? $"Only {variant.AvailableStock} item(s) left in stock."
                        : null;

        return new CartItemDto(
            Guid.Empty,
            variant.ProductId,
            variant.Id,
            variant.ProductName,
            variant.Slug,
            variant.BrandName,
            CatalogQueryHelpers.ResolveImageUrl(_storage, imageFileName),
            CatalogQueryHelpers.ResolveCardImageUrl(_storage, imageFileName),
            variant.ImageAltText,
            variant.Sku,
            variant.ColourName,
            variant.SizeName,
            variant.Price,
            variant.CompareAtPrice,
            CatalogQueryHelpers.CalculateDiscountPercent(variant.Price, variant.CompareAtPrice),
            entry.Quantity,
            variant.Price * entry.Quantity,
            variant.AvailableStock,
            isAvailable,
            isInStock,
            isProductActive && isVariantActive,
            unavailableReason);
    }

    private static CartItemDto BuildItemFromValidation(AddToCartItemDto item)
    {
        var availableStock = item.AvailableStock;
        var isInStock = availableStock > 0;
        var isAvailable = isInStock;

        return new CartItemDto(
            Guid.Empty,
            item.ProductId,
            item.VariantId,
            item.ProductName,
            string.Empty,
            null,
            item.ImageUrl,
            null,
            null,
            item.VariantSku,
            item.ColourName,
            item.SizeName,
            item.UnitPrice,
            item.CompareAtPrice,
            CatalogQueryHelpers.CalculateDiscountPercent(item.UnitPrice, item.CompareAtPrice),
            item.Quantity,
            item.LineTotal,
            availableStock,
            isAvailable,
            isInStock,
            true,
            isAvailable ? null : "This item is unavailable.");
    }

    private static CartViewData BuildView(IReadOnlyList<CartItemDto> items, bool isAuthenticated)
    {
        var availableItems = items.Where(i => i.IsAvailable).ToList();
        var subtotal = availableItems.Sum(i => i.LineTotal);

        return new CartViewData(
            items,
            items.Sum(i => i.Quantity),
            subtotal,
            subtotal.ToString("C2", System.Globalization.CultureInfo.InvariantCulture),
            isAuthenticated,
            items.Any(i => !i.IsAvailable));
    }

    private static CartMutationResult Fail(string message)
    {
        return new CartMutationResult(false, message, 0);
    }
}
