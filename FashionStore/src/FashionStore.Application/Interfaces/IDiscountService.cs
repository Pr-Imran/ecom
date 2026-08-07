using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// The single central pricing and discount service for the storefront. Every price
/// shown to a customer is recomputed here on the server: promotions are applied
/// first (ordered by priority, then stacked when allowed), followed by the applied
/// coupon. All rules (dates, minimum order, maximum discount, usage limits,
/// restrictions) are validated at calculation time, the breakdown is deterministic
/// and totals are clamped so they can never go negative.
/// </summary>
public interface IDiscountService
{
    /// <summary>
    /// Computes the full pricing for a set of cart lines, applying active
    /// promotions and the supplied coupon code (if any). The returned result is
    /// always deterministic and safe to display. A coupon code that fails any rule
    /// is reported through <see cref="CartPricingResult.CouponMessage"/> and simply
    /// not applied; it never throws.
    /// </summary>
    Task<CartPricingResult> CalculateAsync(
        string? userId,
        IReadOnlyList<CartItemDto> items,
        string? couponCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and applies a single coupon against the current cart, returning a
    /// result the storefront can show immediately after the apply action. The
    /// supplied code is normalized and matched case-insensitively; on success the
    /// coupon discount and breakdown are included so the UI can re-render totals
    /// without a full page reload.
    /// </summary>
    Task<CouponApplyResult> ValidateCouponAsync(
        string? userId,
        IReadOnlyList<CartItemDto> items,
        string couponCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a successful coupon redemption, re-checking the total and
    /// per-customer usage limits inside the same transaction so a coupon can never
    /// be over-used under concurrency. Intended for use by checkout once an order
    /// is created. Returns false when a usage limit would be exceeded.
    /// </summary>
    Task<bool> RecordUsageAsync(
        Guid couponId,
        string userId,
        decimal amountDiscounted,
        string? orderId,
        CancellationToken cancellationToken = default);
}
