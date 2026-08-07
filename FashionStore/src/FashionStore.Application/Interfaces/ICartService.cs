using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Customer shopping cart operations. All data access is scoped to the supplied
/// customer id resolved from the authenticated principal; callers must never trust
/// a client-supplied owner id. Prices, names and stock are always recomputed from
/// the catalogue so the client cannot influence displayed values. Every read and
/// mutation re-verifies the product and variant active state, current price,
/// available stock and the maximum quantity.
/// </summary>
public interface ICartService
{
    /// <summary>
    /// Loads the persisted cart for a customer. Lines whose product or variant is
    /// no longer active, or that exceed available stock, are returned flagged as
    /// unavailable so the UI can warn the customer; they are excluded from the
    /// payable subtotal. Expired lines (not updated within the cart lifetime) are
    /// purged as part of the read.
    /// </summary>
    Task<CartViewData> GetCartAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hydrates a set of anonymous (cookie-based) cart references into display
    /// DTOs using live catalogue data. Unavailable entries are flagged the same way
    /// as persisted cart lines. An optional coupon code (from the anonymous coupon
    /// cookie) is evaluated against the cart and reflected in the pricing result.
    /// </summary>
    Task<CartViewData> ResolveAnonymousAsync(
        IReadOnlyList<AnonymousCartEntry> anonymousEntries,
        string? couponCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a coupon against the customer's cart and, when accepted, persists
    /// it as the applied cart coupon so the discount survives reloads. Returns the
    /// server-computed discount, breakdown and a human-readable message.
    /// </summary>
    Task<CouponApplyResult> ApplyCouponAsync(
        string userId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the applied coupon from the customer's cart and returns the
    /// re-computed (promotion-only) totals and breakdown.
    /// </summary>
    Task<CouponApplyResult> RemoveCouponAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds (or combines) a variant into the customer's cart. The product and
    /// variant are validated against current stock and the maximum quantity before
    /// the line is created or the quantity increased. Returns the updated count.
    /// </summary>
    Task<CartMutationResult> AddAsync(
        string userId,
        Guid productId,
        Guid variantId,
        int quantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the quantity of a cart line. The new quantity is validated against
    /// the maximum quantity and available stock. Returns the updated count.
    /// </summary>
    Task<CartMutationResult> UpdateQuantityAsync(
        string userId,
        Guid productId,
        Guid variantId,
        int quantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a single cart line belonging to the customer. Returns the updated
    /// count.
    /// </summary>
    Task<CartMutationResult> RemoveAsync(
        string userId,
        Guid productId,
        Guid variantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every line from the customer's cart. Returns zero count.
    /// </summary>
    Task<CartMutationResult> ClearAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of units currently in the customer's cart (the sum
    /// of all line quantities).
    /// </summary>
    Task<int> GetCountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a set of anonymous cart entries into the customer's persisted cart
    /// after sign-in. Identical product variants are combined, the combined
    /// quantity respects maximum stock and the maximum quantity, duplicate lines
    /// are avoided and unavailable items are reported by skipping them. Returns the
    /// number of entries successfully merged.
    /// </summary>
    Task<int> MergeAsync(
        string userId,
        IReadOnlyList<AnonymousCartEntry> anonymousEntries,
        CancellationToken cancellationToken = default);
}
