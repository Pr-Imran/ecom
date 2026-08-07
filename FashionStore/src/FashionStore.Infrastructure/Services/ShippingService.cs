using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Administrative shipping configuration. Shipping method codes are normalized to
/// upper case so they are unique and matched case-insensitively. Product and
/// category scoping rows are stored as relational join rows and replaced atomically
/// on update (mirroring the coupon restriction handling). Zones, rates and blackout
/// windows are validated server-side before they are persisted.
/// </summary>
public sealed class ShippingService : IShippingService
{
    private const int MaxCodeLength = 50;

    private readonly AppDbContext _context;
    private readonly ILogger<ShippingService> _logger;

    public ShippingService(AppDbContext context, ILogger<ShippingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ---- Shipping methods ----

    public async Task<IReadOnlyList<ShippingMethodDto>> GetMethodsAsync(
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ShippingMethods.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        var methods = await query
            .Include(m => m.ProductRestrictions)
            .Include(m => m.CategoryRestrictions)
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Name)
            .ToListAsync(cancellationToken);

        return methods.Select(ToMethodDto).ToList();
    }

    public async Task<ShippingMethodDto?> GetMethodByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var method = await LoadMethodWithRestrictionsAsync(id, cancellationToken);
        return method == null ? null : ToMethodDto(method);
    }

    public async Task<ShippingMethodDto> CreateMethodAsync(
        CreateShippingMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMethod(request.Code, request.EstimatedMinDays, request.EstimatedMaxDays, request.FreeShippingThreshold, request.MaxPackageWeight);

        var code = NormalizeCode(request.Code);
        if (!await IsMethodCodeUniqueAsync(code, null, cancellationToken))
        {
            throw new InvalidOperationException($"Shipping method code '{request.Code}' already exists.");
        }

        var now = DateTime.UtcNow;
        var method = new ShippingMethod
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = TrimToNull(request.Description),
            Type = request.Type,
            IsActive = true,
            DisplayOrder = 0,
            EstimatedMinDays = request.EstimatedMinDays,
            EstimatedMaxDays = request.EstimatedMaxDays,
            SupportsCashOnDelivery = request.SupportsCashOnDelivery,
            RequiresShippingAddress = request.RequiresShippingAddress,
            FreeShippingThreshold = request.FreeShippingThreshold,
            MaxPackageWeight = request.MaxPackageWeight,
            PickupInstructions = TrimToNull(request.PickupInstructions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        ReplaceRestrictions(method, request.ProductRestrictions, request.CategoryRestrictions);

        _context.ShippingMethods.Add(method);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created shipping method {MethodId} with code {Code}", method.Id, method.Code);
        return ToMethodDto(method);
    }

    public async Task<ShippingMethodDto?> UpdateMethodAsync(
        Guid id,
        UpdateShippingMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMethod(request.Code, request.EstimatedMinDays, request.EstimatedMaxDays, request.FreeShippingThreshold, request.MaxPackageWeight);

        var method = await LoadMethodWithRestrictionsAsync(id, cancellationToken);
        if (method is null)
        {
            return null;
        }

        var code = NormalizeCode(request.Code);
        if (!await IsMethodCodeUniqueAsync(code, id, cancellationToken))
        {
            throw new InvalidOperationException($"Shipping method code '{request.Code}' already exists.");
        }

        method.Code = code;
        method.Name = request.Name.Trim();
        method.Description = TrimToNull(request.Description);
        method.Type = request.Type;
        method.IsActive = request.IsActive;
        method.DisplayOrder = Math.Max(0, request.DisplayOrder);
        method.EstimatedMinDays = request.EstimatedMinDays;
        method.EstimatedMaxDays = request.EstimatedMaxDays;
        method.SupportsCashOnDelivery = request.SupportsCashOnDelivery;
        method.RequiresShippingAddress = request.RequiresShippingAddress;
        method.FreeShippingThreshold = request.FreeShippingThreshold;
        method.MaxPackageWeight = request.MaxPackageWeight;
        method.PickupInstructions = TrimToNull(request.PickupInstructions);
        method.UpdatedAtUtc = DateTime.UtcNow;

        ReplaceRestrictions(method, request.ProductRestrictions, request.CategoryRestrictions);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated shipping method {MethodId}", method.Id);
        return ToMethodDto(method);
    }

    public async Task<bool> SetMethodActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var method = await _context.ShippingMethods.FindAsync(new object[] { id }, cancellationToken);
        if (method is null)
        {
            return false;
        }

        method.IsActive = isActive;
        method.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("{(State)} shipping method {MethodId}", isActive ? "Activated" : "Deactivated", method.Id);
        return true;
    }

    public async Task<bool> ReorderMethodsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        if (orderedIds.Count == 0)
        {
            return false;
        }

        var methods = await _context.ShippingMethods
            .Where(m => orderedIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        if (methods.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < orderedIds.Count; i++)
        {
            var method = methods.FirstOrDefault(m => m.Id == orderedIds[i]);
            if (method is not null)
            {
                method.DisplayOrder = i;
                method.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Reordered {Count} shipping methods", orderedIds.Count);
        return true;
    }

    public async Task<bool> IsMethodCodeUniqueAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCode(code);
        return !await _context.ShippingMethods.AsNoTracking()
            .AnyAsync(m => m.Code == normalized && (excludeId == null || m.Id != excludeId.Value), cancellationToken);
    }

    // ---- Shipping zones ----

    public async Task<IReadOnlyList<ShippingZoneDto>> GetZonesAsync(
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ShippingZones.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(z => z.IsActive);
        }

        var zones = await query
            .Include(z => z.Countries)
            .Include(z => z.Cities)
            .OrderBy(z => z.DisplayOrder)
            .ThenBy(z => z.Name)
            .ToListAsync(cancellationToken);

        return zones.Select(ToZoneDto).ToList();
    }

    public async Task<ShippingZoneDto?> GetZoneByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zone = await LoadZoneWithMembersAsync(id, cancellationToken);
        return zone == null ? null : ToZoneDto(zone);
    }

    public async Task<ShippingZoneDto> CreateZoneAsync(
        CreateShippingZoneRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Shipping zone name is required.");
        }

        var now = DateTime.UtcNow;
        var zone = new ShippingZone
        {
            Name = request.Name.Trim(),
            Description = TrimToNull(request.Description),
            IsActive = true,
            DisplayOrder = Math.Max(0, request.DisplayOrder),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        ReplaceZoneMembers(zone, request.Countries, request.Cities);

        _context.ShippingZones.Add(zone);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created shipping zone {ZoneId} with name {Name}", zone.Id, zone.Name);
        return ToZoneDto(zone);
    }

    public async Task<ShippingZoneDto?> UpdateZoneAsync(
        Guid id,
        UpdateShippingZoneRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Shipping zone name is required.");
        }

        var zone = await LoadZoneWithMembersAsync(id, cancellationToken);
        if (zone is null)
        {
            return null;
        }

        zone.Name = request.Name.Trim();
        zone.Description = TrimToNull(request.Description);
        zone.IsActive = request.IsActive;
        zone.DisplayOrder = Math.Max(0, request.DisplayOrder);
        zone.UpdatedAtUtc = DateTime.UtcNow;

        ReplaceZoneMembers(zone, request.Countries, request.Cities);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated shipping zone {ZoneId}", zone.Id);
        return ToZoneDto(zone);
    }

    public async Task<bool> SetZoneActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var zone = await _context.ShippingZones.FindAsync(new object[] { id }, cancellationToken);
        if (zone is null)
        {
            return false;
        }

        zone.IsActive = isActive;
        zone.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("{(State)} shipping zone {ZoneId}", isActive ? "Activated" : "Deactivated", zone.Id);
        return true;
    }

    public async Task<bool> DeleteZoneAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zone = await LoadZoneWithMembersAsync(id, cancellationToken);
        if (zone is null)
        {
            return false;
        }

        var inUse = await _context.ShippingRates.AsNoTracking()
            .AnyAsync(r => r.ShippingZoneId == id, cancellationToken);
        if (inUse)
        {
            throw new InvalidOperationException("This zone is used by shipping rates and cannot be deleted.");
        }

        _context.ShippingZones.Remove(zone);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted shipping zone {ZoneId}", zone.Id);
        return true;
    }

    // ---- Shipping rates ----

    public async Task<IReadOnlyList<ShippingRateDto>> GetRatesAsync(
        Guid? methodId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ShippingRates.AsNoTracking().AsQueryable();

        if (methodId.HasValue)
        {
            query = query.Where(r => r.ShippingMethodId == methodId.Value);
        }

        var rates = await query
            .OrderBy(r => r.ShippingMethodId)
            .ThenBy(r => r.Priority)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return rates.Select(ToRateDto).ToList();
    }

    public async Task<ShippingRateDto> CreateRateAsync(
        CreateShippingRateRequest request,
        CancellationToken cancellationToken = default)
    {
        await ValidateRateAsync(request.ShippingMethodId, request.ShippingZoneId, request.Amount, request.MinWeightKg, request.MaxWeightKg, cancellationToken);

        var now = DateTime.UtcNow;
        var rate = new ShippingRate
        {
            ShippingMethodId = request.ShippingMethodId,
            ShippingZoneId = request.ShippingZoneId,
            CityName = TrimToNull(request.CityName),
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Delivery" : request.Name.Trim(),
            RateType = request.RateType,
            Amount = request.Amount,
            MinWeightKg = request.MinWeightKg,
            MaxWeightKg = request.MaxWeightKg,
            MinOrderAmount = request.MinOrderAmount,
            Priority = Math.Max(0, request.Priority),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.ShippingRates.Add(rate);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created shipping rate {RateId} for method {MethodId}", rate.Id, rate.ShippingMethodId);
        return ToRateDto(rate);
    }

    public async Task<ShippingRateDto?> UpdateRateAsync(
        Guid id,
        UpdateShippingRateRequest request,
        CancellationToken cancellationToken = default)
    {
        await ValidateRateAsync(request.ShippingMethodId, request.ShippingZoneId, request.Amount, request.MinWeightKg, request.MaxWeightKg, cancellationToken);

        var rate = await _context.ShippingRates.FindAsync(new object[] { id }, cancellationToken);
        if (rate is null)
        {
            return null;
        }

        rate.ShippingMethodId = request.ShippingMethodId;
        rate.ShippingZoneId = request.ShippingZoneId;
        rate.CityName = TrimToNull(request.CityName);
        rate.Name = string.IsNullOrWhiteSpace(request.Name) ? "Delivery" : request.Name.Trim();
        rate.RateType = request.RateType;
        rate.Amount = request.Amount;
        rate.MinWeightKg = request.MinWeightKg;
        rate.MaxWeightKg = request.MaxWeightKg;
        rate.MinOrderAmount = request.MinOrderAmount;
        rate.Priority = Math.Max(0, request.Priority);
        rate.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated shipping rate {RateId}", rate.Id);
        return ToRateDto(rate);
    }

    public async Task<bool> DeleteRateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rate = await _context.ShippingRates.FindAsync(new object[] { id }, cancellationToken);
        if (rate is null)
        {
            return false;
        }

        _context.ShippingRates.Remove(rate);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted shipping rate {RateId}", rate.Id);
        return true;
    }

    // ---- Delivery blackouts ----

    public async Task<IReadOnlyList<DeliveryBlackoutDto>> GetBlackoutsAsync(Guid methodId, CancellationToken cancellationToken = default)
    {
        var blackouts = await _context.DeliveryBlackouts.AsNoTracking()
            .Where(b => b.ShippingMethodId == methodId)
            .OrderByDescending(b => b.StartAtUtc)
            .ToListAsync(cancellationToken);

        return blackouts.Select(ToBlackoutDto).ToList();
    }

    public async Task<DeliveryBlackoutDto> CreateBlackoutAsync(
        CreateDeliveryBlackoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateBlackout(request.ShippingMethodId, request.StartAtUtc, request.EndAtUtc);

        var exists = await _context.ShippingMethods.AsNoTracking()
            .AnyAsync(m => m.Id == request.ShippingMethodId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Shipping method not found.");
        }

        var now = DateTime.UtcNow;
        var blackout = new DeliveryBlackout
        {
            ShippingMethodId = request.ShippingMethodId,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            Reason = TrimToNull(request.Reason),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.DeliveryBlackouts.Add(blackout);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created delivery blackout {BlackoutId} for method {MethodId}", blackout.Id, blackout.ShippingMethodId);
        return ToBlackoutDto(blackout);
    }

    public async Task<DeliveryBlackoutDto?> UpdateBlackoutAsync(
        Guid id,
        UpdateDeliveryBlackoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateBlackout(request.ShippingMethodId, request.StartAtUtc, request.EndAtUtc);

        var blackout = await _context.DeliveryBlackouts.FindAsync(new object[] { id }, cancellationToken);
        if (blackout is null)
        {
            return null;
        }

        blackout.ShippingMethodId = request.ShippingMethodId;
        blackout.StartAtUtc = request.StartAtUtc;
        blackout.EndAtUtc = request.EndAtUtc;
        blackout.Reason = TrimToNull(request.Reason);
        blackout.IsActive = request.IsActive;
        blackout.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated delivery blackout {BlackoutId}", blackout.Id);
        return ToBlackoutDto(blackout);
    }

    public async Task<bool> DeleteBlackoutAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var blackout = await _context.DeliveryBlackouts.FindAsync(new object[] { id }, cancellationToken);
        if (blackout is null)
        {
            return false;
        }

        _context.DeliveryBlackouts.Remove(blackout);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted delivery blackout {BlackoutId}", blackout.Id);
        return true;
    }

    // ---- Private helpers ----

    private async Task<ShippingMethod?> LoadMethodWithRestrictionsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.ShippingMethods
            .Include(m => m.ProductRestrictions)
            .Include(m => m.CategoryRestrictions)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    private async Task<ShippingZone?> LoadZoneWithMembersAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.ShippingZones
            .Include(z => z.Countries)
            .Include(z => z.Cities)
            .FirstOrDefaultAsync(z => z.Id == id, cancellationToken);
    }

    private void ReplaceRestrictions(
        ShippingMethod method,
        IReadOnlyList<ShippingMethodProductRestrictionDto> productRestrictions,
        IReadOnlyList<ShippingMethodCategoryRestrictionDto> categoryRestrictions)
    {
        // Remove the existing join rows through the context instead of relying on
        // DetectChanges over the navigation collections, which would mark newly
        // created client-keyed rows as Modified and throw.
        _context.ShippingMethodProducts.RemoveRange(method.ProductRestrictions);
        _context.ShippingMethodCategories.RemoveRange(method.CategoryRestrictions);

        method.ProductRestrictions.Clear();
        method.CategoryRestrictions.Clear();

        foreach (var restriction in productRestrictions
                     .Where(r => r.ProductId != Guid.Empty)
                     .DistinctBy(r => (r.ProductId, r.IsExclusion)))
        {
            var item = new ShippingMethodProduct
            {
                ShippingMethodId = method.Id,
                ProductId = restriction.ProductId,
                IsExclusion = restriction.IsExclusion
            };
            method.ProductRestrictions.Add(item);
            _context.ShippingMethodProducts.Add(item);
        }

        foreach (var restriction in categoryRestrictions
                     .Where(r => r.CategoryId != Guid.Empty)
                     .DistinctBy(r => (r.CategoryId, r.IsExclusion)))
        {
            var item = new ShippingMethodCategory
            {
                ShippingMethodId = method.Id,
                CategoryId = restriction.CategoryId,
                IsExclusion = restriction.IsExclusion
            };
            method.CategoryRestrictions.Add(item);
            _context.ShippingMethodCategories.Add(item);
        }
    }

    private void ReplaceZoneMembers(
        ShippingZone zone,
        IReadOnlyList<string> countries,
        IReadOnlyList<string> cities)
    {
        _context.ShippingZoneCountries.RemoveRange(zone.Countries);
        _context.ShippingZoneCities.RemoveRange(zone.Cities);

        zone.Countries.Clear();
        zone.Cities.Clear();

        foreach (var code in countries
                     .Select(NormalizeCountryCode)
                     .Where(code => !string.IsNullOrEmpty(code))
                     .Distinct(StringComparer.Ordinal))
        {
            var item = new ShippingZoneCountry
            {
                ShippingZoneId = zone.Id,
                CountryCode = code!
            };
            zone.Countries.Add(item);
            _context.ShippingZoneCountries.Add(item);
        }

        foreach (var city in cities
                     .Select(city => city?.Trim())
                     .Where(city => !string.IsNullOrEmpty(city))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = new ShippingZoneCity
            {
                ShippingZoneId = zone.Id,
                CityName = city!,
                NormalizedCityName = city!.ToUpperInvariant()
            };
            zone.Cities.Add(item);
            _context.ShippingZoneCities.Add(item);
        }
    }

    private async Task ValidateRateAsync(
        Guid methodId,
        Guid? zoneId,
        decimal amount,
        decimal? minWeightKg,
        decimal? maxWeightKg,
        CancellationToken cancellationToken)
    {
        var methodExists = await _context.ShippingMethods.AsNoTracking()
            .AnyAsync(m => m.Id == methodId, cancellationToken);
        if (!methodExists)
        {
            throw new InvalidOperationException("Shipping method not found.");
        }

        if (zoneId.HasValue)
        {
            var zoneExists = await _context.ShippingZones.AsNoTracking()
                .AnyAsync(z => z.Id == zoneId.Value, cancellationToken);
            if (!zoneExists)
            {
                throw new InvalidOperationException("Shipping zone not found.");
            }
        }

        if (amount < 0)
        {
            throw new InvalidOperationException("Rate amount cannot be negative.");
        }

        if (minWeightKg.HasValue && maxWeightKg.HasValue && maxWeightKg.Value < minWeightKg.Value)
        {
            throw new InvalidOperationException("Maximum weight cannot be below the minimum weight.");
        }

        if (minWeightKg.HasValue && minWeightKg.Value < 0)
        {
            throw new InvalidOperationException("Minimum weight cannot be negative.");
        }
    }

    private static void ValidateMethod(
        string code,
        int estimatedMinDays,
        int estimatedMaxDays,
        decimal? freeShippingThreshold,
        decimal? maxPackageWeight)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Shipping method code is required.");
        }

        if (code.Trim().Length > MaxCodeLength)
        {
            throw new InvalidOperationException($"Shipping method code cannot exceed {MaxCodeLength} characters.");
        }

        if (estimatedMinDays < 0 || estimatedMaxDays < estimatedMinDays)
        {
            throw new InvalidOperationException("Estimated delivery days are invalid.");
        }

        if (freeShippingThreshold.HasValue && freeShippingThreshold.Value < 0)
        {
            throw new InvalidOperationException("Free-shipping threshold cannot be negative.");
        }

        if (maxPackageWeight.HasValue && maxPackageWeight.Value < 0)
        {
            throw new InvalidOperationException("Maximum package weight cannot be negative.");
        }
    }

    private static void ValidateBlackout(
        Guid methodId,
        DateTime startAtUtc,
        DateTime endAtUtc)
    {
        if (methodId == Guid.Empty)
        {
            throw new InvalidOperationException("Shipping method is required.");
        }

        if (endAtUtc <= startAtUtc)
        {
            throw new InvalidOperationException("The blackout end must be after the start.");
        }
    }

    private static ShippingMethodDto ToMethodDto(ShippingMethod m)
    {
        return new ShippingMethodDto(
            m.Id,
            m.Code,
            m.Name,
            m.Description,
            m.Type,
            m.IsActive,
            m.DisplayOrder,
            m.EstimatedMinDays,
            m.EstimatedMaxDays,
            m.SupportsCashOnDelivery,
            m.RequiresShippingAddress,
            m.FreeShippingThreshold,
            m.MaxPackageWeight,
            m.PickupInstructions,
            m.CreatedAtUtc,
            m.UpdatedAtUtc,
            m.ProductRestrictions.Select(r => new ShippingMethodProductRestrictionDto(r.ProductId, r.IsExclusion)).ToList(),
            m.CategoryRestrictions.Select(r => new ShippingMethodCategoryRestrictionDto(r.CategoryId, r.IsExclusion)).ToList());
    }

    private static ShippingZoneDto ToZoneDto(ShippingZone z)
    {
        return new ShippingZoneDto(
            z.Id,
            z.Name,
            z.Description,
            z.IsActive,
            z.DisplayOrder,
            z.Countries.Select(c => c.CountryCode).OrderBy(code => code).ToList(),
            z.Cities.Select(c => c.CityName).OrderBy(name => name, StringComparer.Ordinal).ToList(),
            z.CreatedAtUtc,
            z.UpdatedAtUtc);
    }

    private static ShippingRateDto ToRateDto(ShippingRate r)
    {
        return new ShippingRateDto(
            r.Id,
            r.ShippingMethodId,
            r.ShippingZoneId,
            r.CityName,
            r.Name,
            r.RateType,
            r.Amount,
            r.MinWeightKg,
            r.MaxWeightKg,
            r.MinOrderAmount,
            r.Priority);
    }

    private static DeliveryBlackoutDto ToBlackoutDto(DeliveryBlackout b)
    {
        return new DeliveryBlackoutDto(
            b.Id,
            b.ShippingMethodId,
            b.StartAtUtc,
            b.EndAtUtc,
            b.Reason,
            b.IsActive);
    }

    private static string? NormalizeCountryCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
    }

    private static string NormalizeCode(string code)
    {
        return (code ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
