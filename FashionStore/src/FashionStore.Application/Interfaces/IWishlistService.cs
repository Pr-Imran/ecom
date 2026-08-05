using FashionStore.Application.DTOs.Products;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Customer wishlist operations. Every mutation is scoped to a single customer id
/// resolved from the authenticated principal; callers must never trust a
/// client-supplied owner id. All prices, names and stock values are recomputed
/// from the catalogue so the client cannot influence displayed values.
/// </summary>
public interface IWishlistService
{
    /// <summary>
    /// Loads the wishlist for a customer together with the recently-viewed rail.
    /// Items whose product or saved variant is no longer active or has been removed
    /// from the catalogue are excluded from the returned data.
    /// </summary>
    Task<WishlistViewData> GetWishlistAsync(
        string userId,
        IReadOnlyList<Guid>? recentlyViewedIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a product to the customer's wishlist. Duplicate entries (same product
    /// and variant) are ignored rather than created twice. Returns the updated item
    /// count.
    /// </summary>
    Task<WishlistMutationResult> AddAsync(
        string userId,
        Guid productId,
        Guid? variantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a single wishlist entry belonging to the customer by its id.
    /// </summary>
    Task<WishlistMutationResult> RemoveAsync(
        string userId,
        Guid wishlistItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the customer's wishlist entry matching the given product and
    /// optional variant. Used when discarding an item from the UI.
    /// </summary>
    Task<WishlistMutationResult> RemoveByProductAsync(
        string userId,
        Guid productId,
        Guid? variantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of entries currently in the customer's wishlist.
    /// </summary>
    Task<int> GetCountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a wishlist entry for purchase using the add-to-cart rules and, on
    /// success, removes the entry from the wishlist. The returned item carries
    /// server-computed pricing and stock. A failed validation leaves the wishlist
    /// untouched.
    /// </summary>
    Task<WishlistMutationResult> MoveToCartAsync(
        string userId,
        Guid wishlistItemId,
        int quantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hydrates a set of anonymous (cookie-based) wishlist references into display
    /// DTOs using live catalogue data. Entries whose product is no longer active or
    /// has been removed from the catalogue are excluded. Used to render the wishlist
    /// page for visitors who have not signed in.
    /// </summary>
    Task<WishlistViewData> ResolveAnonymousAsync(
        IReadOnlyList<WishlistMutationRequest> anonymousEntries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a set of anonymous wishlist references into the customer's wishlist
    /// after sign-in. Entries that already exist (same product and variant) are
    /// skipped. Returns the number of newly added entries.
    /// </summary>
    Task<int> MergeAsync(
        string userId,
        IReadOnlyList<WishlistMutationRequest> anonymousEntries,
        CancellationToken cancellationToken = default);
}
