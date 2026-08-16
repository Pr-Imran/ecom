using System.Collections.Concurrent;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// The single central pricing and discount service for the storefront.
///
/// Order of operations is always: promotions first, then the applied coupon.
/// Promotions are matched by scope (product / category / brand / collection),
/// ordered by <see cref="Promotion.Priority"/> (lowest value applied first) and a
/// non-stackable promotion stops any further promotion on the same line. At most
/// one coupon is applied per cart; an auto-apply coupon is used only when no code
/// is supplied. Every rule is validated server-side at calculation time, totals
/// are rounded to currency precision and clamped so they can never go negative,
/// and the breakdown is deterministic (stable sort by promotion then coupon).
/// </summary>
public sealed class DiscountService : IDiscountService
{
    private const int CurrencyScale = 2;
    private const decimal Hundred = 100m;

    private readonly AppDbContext _context;
    private readonly ILogger<DiscountService> _logger;

    // Serializes usage recording per coupon within the process so concurrent
    // requests cannot over-use a coupon. A single-instance deployment is fully
    // guarded here; multi-instance deployments should additionally rely on a
    // database-level mechanism at checkout time.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UsageLocks = new();

    public DiscountService(AppDbContext context, ILogger<DiscountService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CartPricingResult> CalculateAsync(
        string? userId,
        IReadOnlyList<CartItemDto> items,
        string? couponCode,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var available = items.Where(i => i.IsAvailable && i.LineTotal > 0).ToList();
        var baseSubtotal = Round(items.Where(i => i.IsAvailable).Sum(i => i.LineTotal));

        var scopes = await LoadProductScopesAsync(available, cancellationToken);
        var promotions = await LoadActivePromotionsAsync(now, cancellationToken);

        var linePromotionDiscounts = new Dictionary<Guid, decimal>();
        var promotionTotals = new Dictionary<Guid, (string Name, decimal Amount)>();

        foreach (var item in available)
        {
            var remaining = item.LineTotal;
            var applicable = MatchPromotions(item, scopes, promotions);
            var blockedByNonStackable = false;

            foreach (var promotion in applicable)
            {
                if (blockedByNonStackable)
                {
                    break;
                }

                var discount = ApplyPromotion(promotion, remaining);
                if (discount <= 0)
                {
                    continue;
                }

                remaining = Round(remaining - discount);
                linePromotionDiscounts[item.VariantId] = Round(
                    (linePromotionDiscounts.TryGetValue(item.VariantId, out var prior) ? prior : 0m) + discount);

                var entry = promotionTotals.GetValueOrDefault(promotion.Id);
                promotionTotals[promotion.Id] = (promotion.Name, Round(entry.Amount + discount));

                if (!promotion.IsStackable)
                {
                    blockedByNonStackable = true;
                }
            }
        }

        var promotionsDiscount = Round(linePromotionDiscounts.Values.Sum());
        var postPromotionTotal = Round(baseSubtotal - promotionsDiscount);
        if (postPromotionTotal < 0)
        {
            promotionsDiscount = Round(promotionsDiscount + postPromotionTotal);
            postPromotionTotal = 0m;
        }

        var coupon = await ResolveCouponAsync(couponCode, now, cancellationToken);
        var hasEnteredCode = !string.IsNullOrWhiteSpace(couponCode);

        Coupon? evaluated = null;
        string? couponMessage = null;

        if (hasEnteredCode && coupon is null)
        {
            couponMessage = "Coupon not found.";
        }
        else if (coupon is not null)
        {
            evaluated = coupon;
        }
        else
        {
            evaluated = await ResolveAutoApplyCouponAsync(
                userId,
                available,
                scopes,
                baseSubtotal,
                promotionsDiscount,
                now,
                cancellationToken);
        }

        CouponValidation? couponValidation = null;
        decimal couponDiscount = 0m;
        bool couponApplied = false;
        string? appliedCouponCode = null;
        bool isFreeShipping = false;

        if (evaluated is not null)
        {
            var validation = await ValidateCouponCoreAsync(
                userId,
                evaluated,
                available,
                scopes,
                baseSubtotal,
                promotionsDiscount,
                now,
                cancellationToken);

            couponValidation = validation;
            if (validation.Success)
            {
                couponDiscount = validation.Discount;
                couponApplied = true;
                appliedCouponCode = evaluated.NormalizedCode;
                isFreeShipping = evaluated.IsFreeShipping;
            }
            else if (hasEnteredCode)
            {
                // Surface the reason only for an explicitly entered code; silent
                // auto-apply failures are dropped without alarming the customer.
                couponMessage = validation.Reason;
            }
        }

        couponDiscount = Round(Math.Max(0m, Math.Min(couponDiscount, postPromotionTotal)));
        var total = Round(Math.Max(0m, postPromotionTotal - couponDiscount));

        var breakdown = BuildBreakdown(promotionTotals.Values, couponApplied, appliedCouponCode, couponDiscount);
        var lines = BuildLinePricing(available, linePromotionDiscounts, couponValidation?.LineDiscounts, postPromotionTotal);

        return new CartPricingResult(
            baseSubtotal,
            promotionsDiscount,
            couponDiscount,
            0m,
            total,
            isFreeShipping,
            couponApplied,
            appliedCouponCode,
            couponMessage,
            breakdown,
            lines);
    }

    public async Task<CouponApplyResult> ValidateCouponAsync(
        string? userId,
        IReadOnlyList<CartItemDto> items,
        string couponCode,
        CancellationToken cancellationToken = default)
    {
        var pricing = await CalculateAsync(userId, items, couponCode, cancellationToken);

        return new CouponApplyResult(
            pricing.CouponApplied,
            pricing.CouponApplied
                ? $"Coupon {pricing.AppliedCouponCode} applied"
                : pricing.CouponMessage ?? "This coupon cannot be applied.",
            pricing.CouponApplied,
            pricing.CouponApplied ? pricing.AppliedCouponCode : null,
            pricing.PromotionsDiscount,
            pricing.CouponDiscount,
            pricing.Total,
            pricing.IsFreeShipping,
            pricing.Breakdown);
    }

    public async Task<bool> RecordUsageAsync(
        Guid couponId,
        string userId,
        decimal amountDiscounted,
        string? orderId,
        CancellationToken cancellationToken = default)
    {
        // Process-local gate: cheap serialization for a single-instance deployment.
        var gate = UsageLocks.GetOrAdd(couponId.ToString(), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Cross-instance gate: the count-then-insert below is only atomic when
            // serialized, and the in-process semaphore does not span app instances.
            // A SQL Server application lock keyed by the coupon serializes usage
            // recording across every replica touching the same database. In-memory
            // test providers cannot execute sp_getapplock, so the app lock is only
            // acquired on a real SQL Server database.
            var useAppLock = _context.Database.IsSqlServer();
            var resource = $"CouponUsage:{couponId:N}";
            if (useAppLock)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 10000",
                    cancellationToken);
            }

            try
            {
                var coupon = await _context.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == couponId, cancellationToken);
                if (coupon is null)
                {
                    return false;
                }

                var totalUsed = await _context.CouponUsages.CountAsync(
                    u => u.CouponId == couponId && u.VoidedAtUtc == null,
                    cancellationToken);
                if (coupon.TotalUsageLimit.HasValue && totalUsed >= coupon.TotalUsageLimit.Value)
                {
                    return false;
                }

                var usedByCustomer = await _context.CouponUsages.CountAsync(
                    u => u.CouponId == couponId && u.UserId == userId && u.VoidedAtUtc == null,
                    cancellationToken);
                if (usedByCustomer >= coupon.PerCustomerLimit)
                {
                    return false;
                }

                _context.CouponUsages.Add(new CouponUsage
                {
                    CouponId = couponId,
                    UserId = userId,
                    OrderId = orderId,
                    AmountDiscounted = Round(Math.Max(0m, amountDiscounted)),
                    UsedAtUtc = DateTime.UtcNow
                });

                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            finally
            {
                if (useAppLock)
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"EXEC sp_releaseapplock @Resource = {resource}, @LockOwner = 'Session'",
                        cancellationToken);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CouponValidation> ValidateCouponCoreAsync(
        string? userId,
        Coupon coupon,
        IReadOnlyList<CartItemDto> available,
        IReadOnlyDictionary<Guid, ProductScope> scopes,
        decimal baseSubtotal,
        decimal promotionsDiscount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!coupon.IsActive)
        {
            return Fail("This coupon is no longer active.");
        }

        if (coupon.StartAtUtc.HasValue && coupon.StartAtUtc.Value > now)
        {
            return Fail("This coupon is not active yet.");
        }

        if (coupon.EndAtUtc.HasValue && coupon.EndAtUtc.Value < now)
        {
            return Fail("This coupon has expired.");
        }

        if (!string.IsNullOrEmpty(coupon.CustomerId))
        {
            if (string.IsNullOrEmpty(userId) || !string.Equals(coupon.CustomerId, userId, StringComparison.Ordinal))
            {
                return Fail("This coupon is only available to selected customers.");
            }
        }

        if (coupon.IsFirstOrderOnly && !string.IsNullOrEmpty(userId))
        {
            var hasPriorOrder = await _context.CouponUsages.AsNoTracking()
                .AnyAsync(u => u.UserId == userId && u.OrderId != null && u.VoidedAtUtc == null, cancellationToken);
            if (hasPriorOrder)
            {
                return Fail("This coupon is only valid on your first order.");
            }
        }

        var totalUsed = await _context.CouponUsages.AsNoTracking()
            .CountAsync(u => u.CouponId == coupon.Id && u.VoidedAtUtc == null, cancellationToken);
        if (coupon.TotalUsageLimit.HasValue && totalUsed >= coupon.TotalUsageLimit.Value)
        {
            return Fail("This coupon has reached its usage limit.");
        }

        if (!string.IsNullOrEmpty(userId))
        {
            var usedByCustomer = await _context.CouponUsages.AsNoTracking()
                .CountAsync(u => u.CouponId == coupon.Id && u.UserId == userId && u.VoidedAtUtc == null, cancellationToken);
            if (usedByCustomer >= coupon.PerCustomerLimit)
            {
                return Fail("You have already used this coupon.");
            }
        }

        if (coupon.MinOrderValue.HasValue && baseSubtotal < coupon.MinOrderValue.Value)
        {
            return Fail($"This coupon requires a minimum order value of {coupon.MinOrderValue.Value:C2}.");
        }

        var eligibleLines = FindEligibleLines(coupon, available, scopes);
        if (eligibleLines.Count == 0)
        {
            return Fail("This coupon does not apply to any items in your cart.");
        }

        var eligibleBase = Round(eligibleLines.Sum(l => l.Base));
        if (eligibleBase <= 0)
        {
            return Fail("This coupon does not apply to any items in your cart.");
        }

        var discount = coupon.DiscountType == DiscountType.Percentage
            ? Round(eligibleBase * coupon.DiscountValue / Hundred)
            : Round(Math.Min(coupon.DiscountValue, eligibleBase));

        if (coupon.MaxDiscountAmount.HasValue)
        {
            discount = Round(Math.Min(discount, coupon.MaxDiscountAmount.Value));
        }

        discount = Round(Math.Max(0m, Math.Min(discount, eligibleBase)));
        var remainingPayable = Round(Math.Max(0m, baseSubtotal - promotionsDiscount));
        discount = Round(Math.Min(discount, remainingPayable));

        var lineDiscounts = AllocateLineDiscounts(eligibleLines, discount);

        return new CouponValidation(
            true,
            null,
            discount,
            lineDiscounts.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static CouponValidation Fail(string reason)
    {
        return new CouponValidation(false, reason, 0m, new Dictionary<Guid, decimal>());
    }

    private List<LineCandidate> FindEligibleLines(
        Coupon coupon,
        IReadOnlyList<CartItemDto> available,
        IReadOnlyDictionary<Guid, ProductScope> scopes)
    {
        var productIds = coupon.CouponProducts.Select(p => p.ProductId).ToHashSet();
        var categoryIds = coupon.CouponCategories.Select(c => c.CategoryId).ToHashSet();
        var brandIds = coupon.CouponBrands.Select(b => b.BrandId).ToHashSet();
        var excludedIds = coupon.CouponExcludedProducts.Select(p => p.ProductId).ToHashSet();

        var hasRestrictions = productIds.Count > 0 || categoryIds.Count > 0 || brandIds.Count > 0;

        var candidates = new List<LineCandidate>();
        foreach (var item in available)
        {
            if (excludedIds.Contains(item.ProductId))
            {
                continue;
            }

            scopes.TryGetValue(item.ProductId, out var scope);
            var matchesRestrictions = true;
            if (hasRestrictions)
            {
                if (productIds.Count > 0 && !productIds.Contains(item.ProductId))
                {
                    matchesRestrictions = false;
                }

                if (matchesRestrictions && categoryIds.Count > 0 &&
                    (scope is null || !categoryIds.Contains(scope.CategoryId)))
                {
                    matchesRestrictions = false;
                }

                if (matchesRestrictions && brandIds.Count > 0)
                {
                    var brandMatches = scope is not null &&
                        scope.BrandId is { } scopeBrandId &&
                        brandIds.Contains(scopeBrandId);
                    if (!brandMatches)
                    {
                        matchesRestrictions = false;
                    }
                }
            }

            if (matchesRestrictions)
            {
                candidates.Add(new LineCandidate(item.VariantId, item.LineTotal));
            }
        }

        return candidates;
    }

    private static List<Promotion> MatchPromotions(
        CartItemDto item,
        IReadOnlyDictionary<Guid, ProductScope> scopes,
        IReadOnlyList<Promotion> promotions)
    {
        scopes.TryGetValue(item.ProductId, out var scope);
        var matches = new List<Promotion>();

        foreach (var promotion in promotions)
        {
            if (item.Quantity < promotion.MinQuantity)
            {
                continue;
            }

            var brandMatches = promotion.BrandId.HasValue &&
                scope?.BrandId is { } scopeBrandId &&
                promotion.BrandId.Value == scopeBrandId;

            var collectionMatches = promotion.CollectionId.HasValue &&
                scope?.CollectionId is { } scopeCollectionId &&
                promotion.CollectionId.Value == scopeCollectionId;

            var scopeMatches =
                (promotion.ProductId.HasValue && promotion.ProductId.Value == item.ProductId) ||
                (promotion.CategoryId.HasValue && scope is not null && promotion.CategoryId.Value == scope.CategoryId) ||
                brandMatches ||
                collectionMatches;

            if (scopeMatches)
            {
                matches.Add(promotion);
            }
        }

        return matches;
    }

    private static decimal ApplyPromotion(Promotion promotion, decimal remaining)
    {
        if (remaining <= 0)
        {
            return 0m;
        }

        var discount = promotion.DiscountType == DiscountType.Percentage
            ? Round(remaining * promotion.DiscountValue / Hundred)
            : promotion.DiscountValue;

        if (promotion.MaxDiscountAmount.HasValue)
        {
            discount = Round(Math.Min(discount, promotion.MaxDiscountAmount.Value));
        }

        return Round(Math.Max(0m, Math.Min(discount, remaining)));
    }

    private static Dictionary<Guid, decimal> AllocateLineDiscounts(
        IReadOnlyList<LineCandidate> eligibleLines,
        decimal totalDiscount)
    {
        var weights = eligibleLines.Select(l => l.Base).ToList();
        var weightSum = weights.Sum();
        if (weightSum <= 0)
        {
            return eligibleLines.ToDictionary(l => l.VariantId, _ => 0m);
        }

        var allocation = new Dictionary<Guid, decimal>();
        var allocated = 0m;
        for (var i = 0; i < eligibleLines.Count; i++)
        {
            var share = Round(eligibleLines[i].Base / weightSum * totalDiscount);
            allocation[eligibleLines[i].VariantId] = share;
            allocated += share;
        }

        // Largest remainder pass keeps the per-line sum exactly equal to the
        // coupon discount after rounding.
        var remainder = Round(totalDiscount - allocated);
        if (remainder != 0m)
        {
            var ordered = eligibleLines
                .Select((l, i) => new { l.VariantId, l.Base, Index = i })
                .OrderByDescending(x => x.Base % 0.01m)
                .ThenBy(x => x.Index)
                .ToList();

            var step = remainder > 0 ? 0.01m : -0.01m;
            foreach (var entry in ordered)
            {
                if (remainder == 0m)
                {
                    break;
                }

                allocation[entry.VariantId] = Round(allocation[entry.VariantId] + step);
                remainder = Round(remainder - step);
            }
        }

        return allocation;
    }

    private static List<CartLinePricing> BuildLinePricing(
        IReadOnlyList<CartItemDto> available,
        Dictionary<Guid, decimal> promotionDiscounts,
        IReadOnlyDictionary<Guid, decimal>? couponDiscounts,
        decimal postPromotionTotal)
    {
        return available.Select(item =>
        {
            var promo = promotionDiscounts.TryGetValue(item.VariantId, out var p) ? p : 0m;
            var coupon = couponDiscounts is not null && couponDiscounts.TryGetValue(item.VariantId, out var c) ? c : 0m;
            coupon = Round(Math.Max(0m, Math.Min(coupon, Math.Max(0m, item.LineTotal - promo))));
            var lineTotal = Round(Math.Max(0m, item.LineTotal - promo - coupon));

            return new CartLinePricing(item.VariantId, item.LineTotal, promo, coupon, lineTotal);
        }).ToList();
    }

    private static List<DiscountBreakdownItem> BuildBreakdown(
        IEnumerable<(string Name, decimal Amount)> promotionEntries,
        bool couponApplied,
        string? appliedCouponCode,
        decimal couponDiscount)
    {
        var breakdown = new List<DiscountBreakdownItem>();

        foreach (var entry in promotionEntries.Where(e => e.Amount > 0))
        {
            breakdown.Add(new DiscountBreakdownItem(entry.Name, entry.Amount, DiscountBreakdownType.Promotion, null));
        }

        if (couponApplied && !string.IsNullOrEmpty(appliedCouponCode) && couponDiscount > 0)
        {
            breakdown.Add(new DiscountBreakdownItem($"Coupon {appliedCouponCode}", couponDiscount, DiscountBreakdownType.Coupon, appliedCouponCode));
        }

        // Deterministic ordering: promotions before the coupon, larger discounts
        // first, then label as a stable tie-breaker.
        return breakdown
            .OrderBy(b => b.Type)
            .ThenByDescending(b => b.Amount)
            .ThenBy(b => b.Label, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<Coupon?> ResolveCouponAsync(
        string? couponCode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            return null;
        }

        var normalized = NormalizeCode(couponCode);
        return await _context.Coupons.AsNoTracking()
            .Include(c => c.CouponCategories)
            .Include(c => c.CouponBrands)
            .Include(c => c.CouponProducts)
            .Include(c => c.CouponExcludedProducts)
            .FirstOrDefaultAsync(c => c.NormalizedCode == normalized, cancellationToken);
    }

    private async Task<Coupon?> ResolveAutoApplyCouponAsync(
        string? userId,
        IReadOnlyList<CartItemDto> available,
        IReadOnlyDictionary<Guid, ProductScope> scopes,
        decimal baseSubtotal,
        decimal promotionsDiscount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var candidates = await _context.Coupons.AsNoTracking()
            .Where(c => c.IsAutoApply &&
                        c.IsActive &&
                        (c.StartAtUtc == null || c.StartAtUtc <= now) &&
                        (c.EndAtUtc == null || c.EndAtUtc >= now))
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        foreach (var candidateId in candidates)
        {
            var coupon = await _context.Coupons.AsNoTracking()
                .Include(c => c.CouponCategories)
                .Include(c => c.CouponBrands)
                .Include(c => c.CouponProducts)
                .Include(c => c.CouponExcludedProducts)
                .FirstOrDefaultAsync(c => c.Id == candidateId, cancellationToken);

            if (coupon is null)
            {
                continue;
            }

            var validation = await ValidateCouponCoreAsync(
                userId,
                coupon,
                available,
                scopes,
                baseSubtotal,
                promotionsDiscount,
                now,
                cancellationToken);

            if (validation.Success)
            {
                return coupon;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<Promotion>> LoadActivePromotionsAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        return await _context.Promotions.AsNoTracking()
            .Where(p => p.IsActive &&
                        (p.StartAtUtc == null || p.StartAtUtc <= now) &&
                        (p.EndAtUtc == null || p.EndAtUtc >= now))
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, ProductScope>> LoadProductScopesAsync(
        List<CartItemDto> available,
        CancellationToken cancellationToken)
    {
        if (available.Count == 0)
        {
            return new Dictionary<Guid, ProductScope>();
        }

        var productIds = available.Select(i => i.ProductId).Distinct().ToList();
        var rows = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new ProductScope(p.Id, p.CategoryId, p.BrandId, p.CollectionId))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ProductId);
    }

    internal static string NormalizeCode(string code)
    {
        return (code ?? string.Empty).Trim().ToUpperInvariant();
    }

    internal static decimal Round(decimal value)
    {
        return Math.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);
    }

    private sealed record CouponValidation(
        bool Success,
        string? Reason,
        decimal Discount,
        IReadOnlyDictionary<Guid, decimal> LineDiscounts);

    private sealed record LineCandidate(Guid VariantId, decimal Base);

    private sealed record ProductScope(Guid ProductId, Guid CategoryId, Guid? BrandId, Guid? CollectionId);
}
