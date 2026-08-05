using FashionStore.Application.DTOs.Home;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Shared projection helpers used by catalogue-facing services to avoid N+1 queries.
/// </summary>
internal static class CatalogQueryHelpers
{
    private const string ColourAttributeName = "Colour";
    private const int LowStockThreshold = 5;

    /// <summary>
    /// Maps each product to the maximum available quantity across its active
    /// variants (stock minus reservations). Products without active variants are
    /// excluded from the dictionary.
    /// </summary>
    public static async Task<Dictionary<Guid, int>> GetStockMapAsync(
        AppDbContext context,
        List<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var rows = await context.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && productIds.Contains(v.ProductId))
            .GroupBy(v => v.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                MaxAvailable = g.Max(v => (v.StockQuantity ?? 0) - (v.ReservedStock ?? 0))
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ProductId, r => r.MaxAvailable);
    }

    /// <summary>
    /// Maps each product to the distinct colours (attribute values belonging to an
    /// attribute named "Colour", or any value with a hex colour) used across its
    /// active variants.
    /// </summary>
    public static async Task<Dictionary<Guid, List<HomeColourDto>>> GetColourMapAsync(
        AppDbContext context,
        List<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, List<HomeColourDto>>();
        }

        var values = await context.ProductVariantAttributeValues
            .AsNoTracking()
            .Include(vav => vav.Variant)
            .Include(vav => vav.AttributeValue)
                .ThenInclude(av => av!.ProductAttribute)
            .Where(vav => productIds.Contains(vav.Variant!.ProductId))
            .ToListAsync(cancellationToken);

        var map = new Dictionary<Guid, List<HomeColourDto>>();

        foreach (var mapping in values)
        {
            var attributeValue = mapping.AttributeValue;
            var attribute = attributeValue?.ProductAttribute;

            if (attributeValue == null || attribute == null)
            {
                continue;
            }

            var isColour = string.Equals(attribute.Name, ColourAttributeName, StringComparison.OrdinalIgnoreCase)
                           || !string.IsNullOrWhiteSpace(attributeValue.HexColour);

            if (!isColour)
            {
                continue;
            }

            if (!map.TryGetValue(mapping.Variant!.ProductId, out var colours))
            {
                colours = new List<HomeColourDto>();
                map[mapping.Variant!.ProductId] = colours;
            }

            var colour = new HomeColourDto(attributeValue.Name, attributeValue.HexColour);
            if (colours.All(c => !string.Equals(c.Name, colour.Name, StringComparison.OrdinalIgnoreCase)))
            {
                colours.Add(colour);
            }
        }

        return map;
    }

    public static string? ResolveImageUrl(IFileStorageService storage, string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : storage.ResolveUrl(fileName);
    }

    public static string? ResolveCardImageUrl(IFileStorageService storage, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var cardPath = ImageDerivatives.BuildDerivativePath(
            fileName,
            ImageDerivatives.All[ImageDerivativeKind.ProductCard]);

        return storage.ResolveUrl(cardPath);
    }

    public static int? CalculateDiscountPercent(decimal price, decimal? compareAtPrice)
    {
        if (!compareAtPrice.HasValue || compareAtPrice.Value <= price || price <= 0)
        {
            return null;
        }

        return (int)Math.Round((compareAtPrice.Value - price) / compareAtPrice.Value * 100);
    }

    public static bool IsRecentlyCreated(DateTime createdAtUtc, bool isNewArrival, DateTime nowUtc)
    {
        return isNewArrival || (nowUtc - createdAtUtc).TotalDays <= 30;
    }

    public static bool IsLowStock(int available) => available <= LowStockThreshold;
}
