using FashionStore.Application.DTOs.Catalog;

namespace FashionStore.Application.DTOs.Products;

/// <summary>
/// A single wishlist entry combining the saved reference with the live product and
/// variant data resolved from the catalogue. Prices, names and stock are always
/// server-computed; the client never supplies trusted pricing.
/// </summary>
public sealed record WishlistItemDto(
    Guid WishlistItemId,
    Guid ProductId,
    Guid? VariantId,
    string ProductName,
    string Slug,
    string? BrandName,
    string? ImageUrl,
    string? ImageCardUrl,
    string? ImageAltText,
    decimal Price,
    decimal? CompareAtPrice,
    int? DiscountPercent,
    string? Sku,
    string? ColourName,
    string? SizeName,
    bool IsInStock,
    int AvailableStock,
    bool IsActive,
    bool RequiresVariation
);

/// <summary>
/// View model for the storefront wishlist page. <see cref="Items"/> are hydrated
/// with live catalogue data; <see cref="RecentlyViewed"/> powers the optional
/// recently-viewed rail rendered beneath the wishlist.
/// </summary>
public sealed record WishlistViewData(
    IReadOnlyList<WishlistItemDto> Items,
    int ItemCount,
    bool IsAuthenticated,
    IReadOnlyList<ProductListItemDto> RecentlyViewed
);

/// <summary>
/// Client-supplied wishlist mutation request. Only identifiers are accepted; all
/// display and pricing data is resolved server-side.
/// </summary>
public sealed record WishlistMutationRequest(
    Guid ProductId,
    Guid? VariantId = null
);

/// <summary>
/// Result of a wishlist mutation containing the updated item count and an optional
/// server-computed item for the affected entry.
/// </summary>
public sealed record WishlistMutationResult(
    bool Success,
    string? ErrorMessage,
    int ItemCount,
    WishlistItemDto? Item = null
);

/// <summary>
/// Removes a persisted wishlist entry by its database id. Only the id is supplied;
/// ownership is enforced against the authenticated principal on the server.
/// </summary>
public sealed record RemoveWishlistItemRequest(
    Guid WishlistItemId
);

/// <summary>
/// Moves a persisted wishlist entry into the shopping cart.
/// </summary>
public sealed record MoveToCartRequestDto(
    Guid WishlistItemId,
    int Quantity
);
