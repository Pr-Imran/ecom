using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Administrative coupon management. Codes are trimmed and upper-cased before
/// storage so they are unique and matched case-insensitively. Restriction and
/// exclusion targets are stored as relational join rows and replaced atomically on
/// update. All values are validated server-side; the client only submits data.
/// </summary>
public sealed class CouponService : ICouponService
{
    private readonly AppDbContext _context;
    private readonly ILogger<CouponService> _logger;

    public CouponService(AppDbContext context, ILogger<CouponService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CouponDto>> GetAllAsync(
        string? status = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var query = _context.Coupons.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.IsActive &&
                (c.StartAtUtc == null || c.StartAtUtc <= now) &&
                (c.EndAtUtc == null || c.EndAtUtc >= now));
        }
        else if (!string.IsNullOrWhiteSpace(status) &&
                 string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.EndAtUtc != null && c.EndAtUtc < now);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            query = query.Where(c => c.Code.Contains(needle) || c.Name.Contains(needle));
        }

        var coupons = await query
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Code)
            .ToListAsync(cancellationToken);

        var usageCounts = await GetUsageCountsAsync(cancellationToken);

        return coupons.Select(c => ToDto(c, usageCounts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<CouponDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await LoadWithRestrictionsAsync(id, cancellationToken);
        if (coupon is null)
        {
            return null;
        }

        return ToDto(coupon, await GetUsageCountAsync(id, cancellationToken));
    }

    public async Task<CouponDto> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.Code, request.DiscountValue, request.PerCustomerLimit, request.StartAtUtc, request.EndAtUtc);

        var normalized = NormalizeCode(request.Code);
        if (!await IsCodeUniqueAsync(normalized, null, cancellationToken))
        {
            throw new InvalidOperationException($"Coupon code '{request.Code}' already exists.");
        }

        var coupon = new Coupon
        {
            Code = request.Code.Trim(),
            NormalizedCode = normalized,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            MaxDiscountAmount = request.MaxDiscountAmount,
            MinOrderValue = request.MinOrderValue,
            IsFreeShipping = request.IsFreeShipping,
            IsActive = true,
            IsAutoApply = request.IsAutoApply,
            IsFirstOrderOnly = request.IsFirstOrderOnly,
            TotalUsageLimit = request.TotalUsageLimit,
            PerCustomerLimit = request.PerCustomerLimit,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            CustomerId = string.IsNullOrWhiteSpace(request.CustomerId) ? null : request.CustomerId.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        ReplaceRestrictions(coupon, request.CategoryIds, request.BrandIds, request.ProductIds, request.ExcludedProductIds);

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created coupon {CouponId} with code {Code}", coupon.Id, coupon.Code);
        return ToDto(coupon, 0);
    }

    public async Task<CouponDto?> UpdateAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.Code, request.DiscountValue, request.PerCustomerLimit, request.StartAtUtc, request.EndAtUtc);

        var coupon = await LoadWithRestrictionsAsync(id, cancellationToken);
        if (coupon is null)
        {
            return null;
        }

        var normalized = NormalizeCode(request.Code);
        if (!await IsCodeUniqueAsync(normalized, id, cancellationToken))
        {
            throw new InvalidOperationException($"Coupon code '{request.Code}' already exists.");
        }

        coupon.Code = request.Code.Trim();
        coupon.NormalizedCode = normalized;
        coupon.Name = request.Name.Trim();
        coupon.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        coupon.DiscountType = request.DiscountType;
        coupon.DiscountValue = request.DiscountValue;
        coupon.MaxDiscountAmount = request.MaxDiscountAmount;
        coupon.MinOrderValue = request.MinOrderValue;
        coupon.IsFreeShipping = request.IsFreeShipping;
        coupon.IsAutoApply = request.IsAutoApply;
        coupon.IsFirstOrderOnly = request.IsFirstOrderOnly;
        coupon.TotalUsageLimit = request.TotalUsageLimit;
        coupon.PerCustomerLimit = request.PerCustomerLimit;
        coupon.StartAtUtc = request.StartAtUtc;
        coupon.EndAtUtc = request.EndAtUtc;
        coupon.CustomerId = string.IsNullOrWhiteSpace(request.CustomerId) ? null : request.CustomerId.Trim();
        coupon.UpdatedAtUtc = DateTime.UtcNow;

        ReplaceRestrictions(coupon, request.CategoryIds, request.BrandIds, request.ProductIds, request.ExcludedProductIds);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated coupon {CouponId} with code {Code}", coupon.Id, coupon.Code);
        return ToDto(coupon, await GetUsageCountAsync(coupon.Id, cancellationToken));
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var coupon = await _context.Coupons.FindAsync(new object[] { id }, cancellationToken);
        if (coupon is null)
        {
            return false;
        }

        coupon.IsActive = isActive;
        coupon.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("{(State)} coupon {CouponId}", isActive ? "Activated" : "Deactivated", coupon.Id);
        return true;
    }

    public async Task<CouponDto?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await LoadWithRestrictionsAsync(id, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var baseCode = NormalizeCode(source.Code);
        var newCode = await DeriveUniqueCodeAsync(baseCode, cancellationToken);

        var copy = new Coupon
        {
            Code = newCode,
            NormalizedCode = NormalizeCode(newCode),
            Name = $"{source.Name} (Copy)",
            Description = source.Description,
            DiscountType = source.DiscountType,
            DiscountValue = source.DiscountValue,
            MaxDiscountAmount = source.MaxDiscountAmount,
            MinOrderValue = source.MinOrderValue,
            IsFreeShipping = source.IsFreeShipping,
            IsActive = false,
            IsAutoApply = source.IsAutoApply,
            IsFirstOrderOnly = source.IsFirstOrderOnly,
            TotalUsageLimit = source.TotalUsageLimit,
            PerCustomerLimit = source.PerCustomerLimit,
            StartAtUtc = source.StartAtUtc,
            EndAtUtc = source.EndAtUtc,
            CustomerId = source.CustomerId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        ReplaceRestrictions(
            copy,
            source.CouponCategories.Select(c => c.CategoryId).ToList(),
            source.CouponBrands.Select(b => b.BrandId).ToList(),
            source.CouponProducts.Select(p => p.ProductId).ToList(),
            source.CouponExcludedProducts.Select(p => p.ProductId).ToList());

        _context.Coupons.Add(copy);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Duplicated coupon {SourceId} into {CouponId}", source.Id, copy.Id);
        return ToDto(copy, 0);
    }

    public async Task<bool> IsCodeUniqueAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCode(code);
        return !await _context.Coupons.AsNoTracking()
            .AnyAsync(c => c.NormalizedCode == normalized && (excludeId == null || c.Id != excludeId.Value), cancellationToken);
    }

    public async Task<int> GetUsageCountAsync(Guid couponId, CancellationToken cancellationToken = default)
    {
        return await _context.CouponUsages.AsNoTracking()
            .CountAsync(u => u.CouponId == couponId, cancellationToken);
    }

    public async Task<IReadOnlyList<CouponUsageDto>> GetUsageAsync(
        Guid? couponId = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CouponUsages.AsNoTracking()
            .Include(u => u.Coupon)
            .AsQueryable();

        if (couponId.HasValue)
        {
            query = query.Where(u => u.CouponId == couponId.Value);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(u => u.UserId == userId);
        }

        var usages = await query
            .OrderByDescending(u => u.UsedAtUtc)
            .ToListAsync(cancellationToken);

        var emails = await LoadUserEmailsAsync(usages.Select(u => u.UserId).Distinct().ToList(), cancellationToken);

        return usages.Select(u => new CouponUsageDto(
            u.Id,
            u.CouponId,
            u.Coupon != null ? u.Coupon.Code : string.Empty,
            u.UserId,
            emails.GetValueOrDefault(u.UserId),
            u.OrderId,
            u.AmountDiscounted,
            u.UsedAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<CouponUsageDto>> GetCustomerUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await GetUsageAsync(null, userId, cancellationToken);
    }

    private async Task<string> DeriveUniqueCodeAsync(string baseCode, CancellationToken cancellationToken)
    {
        var suffix = "-COPY";
        var candidate = baseCode.Length + suffix.Length <= 50 ? baseCode + suffix : baseCode[..Math.Max(1, 50 - suffix.Length)] + suffix;
        while (!await IsCodeUniqueAsync(candidate, null, cancellationToken))
        {
            candidate = candidate.Length >= 50 ? candidate[..49] + "X" : candidate + "X";
        }

        return candidate;
    }

    private async Task<Coupon?> LoadWithRestrictionsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Coupons
            .Include(c => c.CouponCategories)
            .Include(c => c.CouponBrands)
            .Include(c => c.CouponProducts)
            .Include(c => c.CouponExcludedProducts)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    private static void ReplaceRestrictions(
        Coupon coupon,
        IReadOnlyList<Guid> categoryIds,
        IReadOnlyList<Guid> brandIds,
        IReadOnlyList<Guid> productIds,
        IReadOnlyList<Guid> excludedProductIds)
    {
        coupon.CouponCategories.Clear();
        coupon.CouponBrands.Clear();
        coupon.CouponProducts.Clear();
        coupon.CouponExcludedProducts.Clear();

        foreach (var id in categoryIds.Distinct())
        {
            coupon.CouponCategories.Add(new CouponCategory { CouponId = coupon.Id, CategoryId = id });
        }

        foreach (var id in brandIds.Distinct())
        {
            coupon.CouponBrands.Add(new CouponBrand { CouponId = coupon.Id, BrandId = id });
        }

        foreach (var id in productIds.Distinct())
        {
            coupon.CouponProducts.Add(new CouponProduct { CouponId = coupon.Id, ProductId = id });
        }

        foreach (var id in excludedProductIds.Distinct())
        {
            coupon.CouponExcludedProducts.Add(new CouponExcludedProduct { CouponId = coupon.Id, ProductId = id });
        }
    }

    private async Task<Dictionary<Guid, int>> GetUsageCountsAsync(CancellationToken cancellationToken)
    {
        return await _context.CouponUsages.AsNoTracking()
            .GroupBy(u => u.CouponId)
            .Select(g => new { CouponId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CouponId, x => x.Count, cancellationToken);
    }

    private async Task<Dictionary<string, string?>> LoadUserEmailsAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => (string?)u.Email, cancellationToken);
    }

    private static CouponDto ToDto(Coupon c, int usageCount)
    {
        return new CouponDto(
            c.Id,
            c.Code,
            c.Name,
            c.Description,
            c.DiscountType,
            c.DiscountValue,
            c.MaxDiscountAmount,
            c.MinOrderValue,
            c.IsFreeShipping,
            c.IsActive,
            c.IsAutoApply,
            c.IsFirstOrderOnly,
            c.TotalUsageLimit,
            c.PerCustomerLimit,
            c.StartAtUtc,
            c.EndAtUtc,
            c.CustomerId,
            usageCount,
            c.CreatedAtUtc,
            c.CouponCategories.Select(x => x.CategoryId).ToList(),
            c.CouponBrands.Select(x => x.BrandId).ToList(),
            c.CouponProducts.Select(x => x.ProductId).ToList(),
            c.CouponExcludedProducts.Select(x => x.ProductId).ToList());
    }

    private static void ValidateRequest(
        string code,
        decimal discountValue,
        int perCustomerLimit,
        DateTime? startAtUtc,
        DateTime? endAtUtc)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Coupon code is required.");
        }

        if (discountValue <= 0)
        {
            throw new InvalidOperationException("Discount value must be greater than zero.");
        }

        if (perCustomerLimit < 1)
        {
            throw new InvalidOperationException("Per-customer usage limit must be at least 1.");
        }

        if (startAtUtc.HasValue && endAtUtc.HasValue && endAtUtc.Value < startAtUtc.Value)
        {
            throw new InvalidOperationException("End date must be after the start date.");
        }
    }

    internal static string NormalizeCode(string code)
    {
        return (code ?? string.Empty).Trim().ToUpperInvariant();
    }
}
