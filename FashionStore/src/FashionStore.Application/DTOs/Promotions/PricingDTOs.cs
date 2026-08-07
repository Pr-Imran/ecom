namespace FashionStore.Application.DTOs.Promotions;

/// <summary>
/// Origin of a discount line in a pricing breakdown.
/// </summary>
public enum DiscountBreakdownType
{
    Promotion = 0,
    Coupon = 1
}

/// <summary>
/// A single discount line for the order summary breakdown. Every discount applied
/// is listed so the customer can see exactly what the engine deducted and why.
/// </summary>
public sealed record DiscountBreakdownItem(
    string Label,
    decimal Amount,
    DiscountBreakdownType Type,
    string? CouponCode
);

/// <summary>
/// Per-cart-line pricing output of the discount engine. Coupon discounts are
/// attributed to the eligible lines only (product / category / brand scope), which
/// keeps the breakdown truthful when a coupon targets part of the cart.
/// </summary>
public sealed record CartLinePricing(
    Guid VariantId,
    decimal LineSubtotal,
    decimal PromotionDiscount,
    decimal CouponDiscount,
    decimal LineTotal
);

/// <summary>
/// Server-computed pricing for the whole cart: promotions first (ordered by
/// priority), then the applied coupon, all clamped so the total can never go
/// negative. <see cref="CouponMessage"/> explains a rejected or dropped coupon so
/// the UI can surface the exact reason.
/// </summary>
public sealed record CartPricingResult(
    decimal Subtotal,
    decimal PromotionsDiscount,
    decimal CouponDiscount,
    decimal ShippingDiscount,
    decimal Total,
    bool IsFreeShipping,
    bool CouponApplied,
    string? AppliedCouponCode,
    string? CouponMessage,
    IReadOnlyList<DiscountBreakdownItem> Breakdown,
    IReadOnlyList<CartLinePricing> Lines
);

/// <summary>
/// Result of a coupon apply / remove action so the storefront can immediately show
/// the discounted total and the breakdown without a full page reload.
/// </summary>
public sealed record CouponApplyResult(
    bool Success,
    string Message,
    bool CouponApplied,
    string? AppliedCouponCode,
    decimal PromotionsDiscount,
    decimal CouponDiscount,
    decimal Total,
    bool IsFreeShipping,
    IReadOnlyList<DiscountBreakdownItem> Breakdown
);
