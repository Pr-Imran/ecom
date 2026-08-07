using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Promotions;

/// <summary>
/// Coupon as exposed to administrators and the storefront. All values are
/// server-managed; the client only ever submits a coupon code, never prices or
/// limits.
/// </summary>
public sealed record CouponDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinOrderValue,
    bool IsFreeShipping,
    bool IsActive,
    bool IsAutoApply,
    bool IsFirstOrderOnly,
    int? TotalUsageLimit,
    int PerCustomerLimit,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    string? CustomerId,
    int UsageCount,
    DateTime CreatedAtUtc,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<Guid> BrandIds,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<Guid> ExcludedProductIds
);

/// <summary>
/// Request used to create a coupon. <see cref="Code"/> is normalized to upper case
/// before it is stored so matching is case-insensitive.
/// </summary>
public sealed record CreateCouponRequest(
    string Code,
    string Name,
    string? Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinOrderValue,
    bool IsFreeShipping,
    bool IsAutoApply,
    bool IsFirstOrderOnly,
    int? TotalUsageLimit,
    int PerCustomerLimit,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    string? CustomerId,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<Guid> BrandIds,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<Guid> ExcludedProductIds
);

/// <summary>
/// Request used to update an existing coupon. Supports every field that can be
/// changed after creation; activation state is toggled separately.
/// </summary>
public sealed record UpdateCouponRequest(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinOrderValue,
    bool IsFreeShipping,
    bool IsAutoApply,
    bool IsFirstOrderOnly,
    int? TotalUsageLimit,
    int PerCustomerLimit,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    string? CustomerId,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<Guid> BrandIds,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<Guid> ExcludedProductIds
);

/// <summary>
/// A single coupon redemption used for admin usage history and customer usage
/// reporting.
/// </summary>
public sealed record CouponUsageDto(
    Guid Id,
    Guid CouponId,
    string CouponCode,
    string UserId,
    string? UserEmail,
    string? OrderId,
    decimal AmountDiscounted,
    DateTime UsedAtUtc
);
