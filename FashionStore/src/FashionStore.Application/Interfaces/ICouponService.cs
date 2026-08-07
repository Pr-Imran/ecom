using FashionStore.Application.DTOs.Promotions;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Administrative coupon management: list with active / expired filtering, create,
/// update, duplicate, activate / deactivate and usage history. Codes are normalized
/// to upper case so they are unique and matched case-insensitively.
/// </summary>
public interface ICouponService
{
    /// <summary>
    /// Lists coupons. <paramref name="status"/> filters by "active" (currently
    /// valid dates and active flag) or "expired" (end date passed), otherwise all
    /// coupons are returned. Each entry includes its current usage count.
    /// </summary>
    Task<IReadOnlyList<CouponDto>> GetAllAsync(
        string? status = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single coupon with its restriction id lists and usage count, or
    /// null when no such coupon exists.
    /// </summary>
    Task<CouponDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a coupon after normalizing and de-duplicating the code. Throws
    /// <see cref="InvalidOperationException"/> when the code is already in use.
    /// </summary>
    Task<CouponDto> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing coupon. Throws <see cref="InvalidOperationException"/>
    /// when the code collides with another coupon. Returns null when the coupon
    /// does not exist.
    /// </summary>
    Task<CouponDto?> UpdateAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates or deactivates a coupon. Returns false when the coupon does not
    /// exist.
    /// </summary>
    Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a copy of an existing coupon with a unique code derived from the
    /// original (e.g. "SAVE10" becomes "SAVE10-COPY"). Returns null when the
    /// original does not exist.
    /// </summary>
    Task<CouponDto?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when the code is available (not used by any other coupon),
    /// optionally ignoring a specific coupon id during updates.
    /// </summary>
    Task<bool> IsCodeUniqueAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of times a coupon has been redeemed.
    /// </summary>
    Task<int> GetUsageCountAsync(Guid couponId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns redemption history, optionally filtered to a single coupon or a
    /// single customer.
    /// </summary>
    Task<IReadOnlyList<CouponUsageDto>> GetUsageAsync(
        Guid? couponId = null,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the coupons applied by a single customer together with their usage
    /// counts, for the customer usage view.
    /// </summary>
    Task<IReadOnlyList<CouponUsageDto>> GetCustomerUsageAsync(string userId, CancellationToken cancellationToken = default);
}
