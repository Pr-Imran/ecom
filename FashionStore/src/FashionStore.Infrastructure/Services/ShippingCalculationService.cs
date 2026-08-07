using FashionStore.Application.Common;
using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// The server-side shipping calculation engine. It reloads live product weights
/// and categories from the database for every quote so the client cannot influence
/// pricing, then resolves the destination against the configured zones, applies
/// product / category restrictions, weight bands, maximum package weight, blackout
/// windows and free-shipping thresholds and returns priced quotes. The checkout
/// must call this service with the server-resolved cart and never trust a shipping
/// cost submitted by the browser.
/// </summary>
public sealed class ShippingCalculationService : IShippingCalculationService
{
    private const int CityRateSpecificity = 2;
    private const int ZoneRateSpecificity = 1;
    private const int GlobalRateSpecificity = 0;

    private readonly AppDbContext _context;
    private readonly ILogger<ShippingCalculationService> _logger;

    public ShippingCalculationService(AppDbContext context, ILogger<ShippingCalculationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ShippingQuoteResultDto> QuoteAsync(
        ShippingCalculationInput input,
        CancellationToken cancellationToken = default)
    {
        var countryCode = (input.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        var city = string.IsNullOrWhiteSpace(input.City) ? null : input.City.Trim();
        var normalizedCity = city?.ToUpperInvariant();
        var now = DateTime.UtcNow;

        if (!CountryCatalog.IsKnown(countryCode))
        {
            return Unsupported($"We do not recognize the destination country '{input.CountryCode}'.", []);
        }

        var methods = await LoadActiveMethodsAsync(cancellationToken);
        if (methods.Count == 0)
        {
            return Unsupported("We are not currently accepting orders.", []);
        }

        var zones = await LoadActiveZonesAsync(cancellationToken);
        var blackouts = await LoadActiveBlackoutsAsync(now, cancellationToken);
        var resolvedZoneId = ResolveZone(zones, countryCode, normalizedCity)?.Id;

        var coverageSupported = IsDestinationSupported(zones, methods, countryCode);
        if (!coverageSupported)
        {
            return Unsupported($"We do not currently deliver to {countryCode}.", []);
        }

        var lineContext = await LoadLineContextAsync(input.Lines, cancellationToken);

        var quotes = new List<ShippingQuoteDto>(methods.Count);
        foreach (var method in methods)
        {
            quotes.Add(BuildQuote(
                method,
                input,
                normalizedCity,
                resolvedZoneId,
                blackouts,
                lineContext));
        }

        _logger.LogInformation(
            "Quoted {Count} shipping methods for {Country} to {City}",
            quotes.Count,
            countryCode,
            city ?? "(no city)");

        return new ShippingQuoteResultDto(true, null, quotes);
    }

    // ---- Private helpers ----

    private async Task<List<ShippingMethod>> LoadActiveMethodsAsync(CancellationToken cancellationToken)
    {
        return await _context.ShippingMethods.AsNoTracking()
            .Where(m => m.IsActive)
            .Include(m => m.Rates)
            .Include(m => m.ProductRestrictions)
            .Include(m => m.CategoryRestrictions)
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Name)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<ShippingZone>> LoadActiveZonesAsync(CancellationToken cancellationToken)
    {
        return await _context.ShippingZones.AsNoTracking()
            .Where(z => z.IsActive)
            .Include(z => z.Countries)
            .Include(z => z.Cities)
            .OrderBy(z => z.DisplayOrder)
            .ThenBy(z => z.Name)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<DeliveryBlackout>> LoadActiveBlackoutsAsync(DateTime now, CancellationToken cancellationToken)
    {
        return await _context.DeliveryBlackouts.AsNoTracking()
            .Where(b => b.IsActive && b.StartAtUtc <= now && b.EndAtUtc >= now)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the most specific zone for a destination. A city match wins over a
    /// country match, and a zone with no country / city members is global.
    /// </summary>
    private static ShippingZone? ResolveZone(IReadOnlyList<ShippingZone> zones, string countryCode, string? normalizedCity)
    {
        ShippingZone? cityMatch = null;
        ShippingZone? countryMatch = null;

        foreach (var zone in zones)
        {
            var coversCountry = zone.Countries.Count == 0 ||
                                zone.Countries.Any(c => string.Equals(c.CountryCode, countryCode, StringComparison.Ordinal));
            var coversCity = !string.IsNullOrEmpty(normalizedCity) &&
                             zone.Cities.Any(c => string.Equals(c.NormalizedCityName, normalizedCity, StringComparison.Ordinal));

            if (coversCountry && coversCity && cityMatch is null)
            {
                cityMatch = zone;
            }
            else if (coversCountry && zone.Cities.Count == 0 && countryMatch is null)
            {
                countryMatch = zone;
            }
        }

        return cityMatch ?? countryMatch;
    }

    private static bool IsDestinationSupported(
        IReadOnlyList<ShippingZone> zones,
        IReadOnlyList<ShippingMethod> methods,
        string countryCode)
    {
        var hasGlobalZone = zones.Any(z => z.Countries.Count == 0 && z.Cities.Count == 0);
        if (hasGlobalZone)
        {
            return true;
        }

        var coversCountry = zones.Any(z => z.Countries.Any(c => string.Equals(c.CountryCode, countryCode, StringComparison.Ordinal)));
        if (coversCountry)
        {
            return true;
        }

        var hasGlobalRate = methods.Any(m => m.Rates.Any(r => r.ShippingZoneId is null));
        return hasGlobalRate;
    }

    private async Task<LineContext> LoadLineContextAsync(IReadOnlyList<ShippingLineInput> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return new LineContext(0m, new HashSet<Guid>(), new HashSet<Guid>());
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Weight, p.CategoryId })
            .ToListAsync(cancellationToken);

        var productLookup = products.ToDictionary(p => p.Id, p => p);
        var totalWeight = 0m;
        var cartProductIds = new HashSet<Guid>();
        var cartCategoryIds = new HashSet<Guid>();

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                continue;
            }

            cartProductIds.Add(line.ProductId);
            if (productLookup.TryGetValue(line.ProductId, out var product))
            {
                totalWeight += (product.Weight ?? 0m) * line.Quantity;
                cartCategoryIds.Add(product.CategoryId);
            }
        }

        return new LineContext(totalWeight, cartProductIds, cartCategoryIds);
    }

    private ShippingQuoteDto BuildQuote(
        ShippingMethod method,
        ShippingCalculationInput input,
        string? normalizedCity,
        Guid? resolvedZoneId,
        IReadOnlyList<DeliveryBlackout> blackouts,
        LineContext lines)
    {
        if (blackouts.Any(b => b.ShippingMethodId == method.Id))
        {
            return ToQuote(method, unavailable: "Temporarily unavailable for delivery.");
        }

        if (method.MaxPackageWeight.HasValue && lines.TotalWeightKg > method.MaxPackageWeight.Value)
        {
            return ToQuote(method, unavailable: "This delivery method cannot carry the weight of your order.");
        }

        var restrictionReason = EvaluateRestrictions(method, lines);
        if (restrictionReason is not null)
        {
            return ToQuote(method, unavailable: restrictionReason);
        }

        var rate = ResolveRate(method, resolvedZoneId, normalizedCity, lines.TotalWeightKg, input.Subtotal);
        if (rate is null)
        {
            return ToQuote(method, unavailable: "No delivery option is configured for your destination.");
        }

        var price = rate.RateType == FashionStore.Domain.Enums.ShippingRateType.PerUnitWeight
            ? Math.Round(rate.Amount * lines.TotalWeightKg, 2)
            : rate.Amount;

        var isFreeByCoupon = input.CouponFreeShipping;
        var isFreeByThreshold = method.FreeShippingThreshold.HasValue && input.Subtotal >= method.FreeShippingThreshold.Value;
        var isFree = isFreeByCoupon || isFreeByThreshold;

        decimal? remainingForFreeShipping = null;
        if (method.FreeShippingThreshold.HasValue && !isFree)
        {
            remainingForFreeShipping = Math.Max(0m, method.FreeShippingThreshold.Value - input.Subtotal);
        }

        return new ShippingQuoteDto(
            method.Id,
            method.Code,
            method.Name,
            method.Description,
            method.Type,
            isFree ? 0m : price,
            isFree,
            true,
            null,
            method.EstimatedMinDays,
            method.EstimatedMaxDays,
            method.SupportsCashOnDelivery,
            method.FreeShippingThreshold,
            remainingForFreeShipping,
            method.PickupInstructions);
    }

    private static string? EvaluateRestrictions(ShippingMethod method, LineContext lines)
    {
        var excludedProducts = method.ProductRestrictions.Where(r => r.IsExclusion).Select(r => r.ProductId).ToHashSet();
        if (excludedProducts.Count > 0 && lines.CartProductIds.Overlaps(excludedProducts))
        {
            return "This delivery method is not available for one of your items.";
        }

        var excludedCategories = method.CategoryRestrictions.Where(r => r.IsExclusion).Select(r => r.CategoryId).ToHashSet();
        if (excludedCategories.Count > 0 && lines.CartCategoryIds.Overlaps(excludedCategories))
        {
            return "This delivery method is not available for one of your items.";
        }

        var includedProducts = method.ProductRestrictions.Where(r => !r.IsExclusion).Select(r => r.ProductId).ToHashSet();
        if (includedProducts.Count > 0 && !lines.CartProductIds.Overlaps(includedProducts))
        {
            return "This delivery method is only available for select items in your cart.";
        }

        var includedCategories = method.CategoryRestrictions.Where(r => !r.IsExclusion).Select(r => r.CategoryId).ToHashSet();
        if (includedCategories.Count > 0 && !lines.CartCategoryIds.Overlaps(includedCategories))
        {
            return "This delivery method is only available for select items in your cart.";
        }

        return null;
    }

    /// <summary>
    /// Picks the best rate for a destination: city-level overrides beat zone rates,
    /// zone rates beat global fallbacks, and lower priorities win within a level.
    /// Only rates whose weight band and minimum order amount match are considered.
    /// </summary>
    private static ShippingRate? ResolveRate(
        ShippingMethod method,
        Guid? resolvedZoneId,
        string? normalizedCity,
        decimal totalWeightKg,
        decimal subtotal)
    {
        ShippingRate? best = null;
        var bestPriority = int.MaxValue;
        var bestSpecificity = -1;

        foreach (var rate in method.Rates)
        {
            if (rate.ShippingZoneId.HasValue && rate.ShippingZoneId.Value != resolvedZoneId)
            {
                continue;
            }

            var hasCityScope = !string.IsNullOrEmpty(rate.CityName);
            if (hasCityScope && (string.IsNullOrEmpty(normalizedCity) || !string.Equals(rate.CityName!.Trim(), normalizedCity, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (rate.MinWeightKg.HasValue && totalWeightKg < rate.MinWeightKg.Value)
            {
                continue;
            }

            if (rate.MaxWeightKg.HasValue && totalWeightKg > rate.MaxWeightKg.Value)
            {
                continue;
            }

            if (rate.MinOrderAmount.HasValue && subtotal < rate.MinOrderAmount.Value)
            {
                continue;
            }

            var specificity = hasCityScope
                ? CityRateSpecificity
                : rate.ShippingZoneId.HasValue ? ZoneRateSpecificity : GlobalRateSpecificity;

            if (rate.Priority < bestPriority ||
                (rate.Priority == bestPriority && specificity > bestSpecificity))
            {
                best = rate;
                bestPriority = rate.Priority;
                bestSpecificity = specificity;
            }
        }

        return best;
    }

    private static ShippingQuoteDto ToQuote(ShippingMethod method, string unavailable)
    {
        return new ShippingQuoteDto(
            method.Id,
            method.Code,
            method.Name,
            method.Description,
            method.Type,
            0m,
            false,
            false,
            unavailable,
            method.EstimatedMinDays,
            method.EstimatedMaxDays,
            method.SupportsCashOnDelivery,
            method.FreeShippingThreshold,
            null,
            method.PickupInstructions);
    }

    private static ShippingQuoteResultDto Unsupported(string reason, IReadOnlyList<ShippingQuoteDto> quotes)
    {
        return new ShippingQuoteResultDto(false, reason, quotes);
    }

    private sealed record LineContext(
        decimal TotalWeightKg,
        HashSet<Guid> CartProductIds,
        HashSet<Guid> CartCategoryIds);
}
