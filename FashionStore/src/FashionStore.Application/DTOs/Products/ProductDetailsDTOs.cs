using FashionStore.Application.DTOs.Catalog;

namespace FashionStore.Application.DTOs.Products;

/// <summary>
/// Aggregate view model for the public storefront product details page. All prices,
/// stock and discount values are server-computed and never trusted from the client.
/// </summary>
public sealed record ProductDetailsData(
    Guid Id,
    string Name,
    string Slug,
    string? ShortDescription,
    string? FullDescription,
    string? BrandName,
    string? CategoryName,
    string? CategorySlug,
    string? CollectionName,
    string? CollectionSlug,
    string? Material,
    string? Fabric,
    string? CareInstructions,
    string? Gender,
    string? CountryOfOrigin,
    string BaseSku,
    decimal Price,
    decimal? CompareAtPrice,
    int? DiscountPercent,
    bool AllowReviews,
    double AverageRating,
    int ReviewCount,
    bool IsInStock,
    int AvailableStock,
    bool HasVideo,
    string? VideoUrl,
    string? SeoTitle,
    string? SeoDescription,
    IReadOnlyList<ProductDetailsImageDto> Images,
    StorefrontProductVariationsDto Variations,
    IReadOnlyList<Guid>? DefaultVariantAttributeValueIds,
    IReadOnlyList<ProductListItemDto> RelatedProducts,
    IReadOnlyList<ProductListItemDto> RecentlyViewed
);

public sealed record ProductDetailsImageDto(
    Guid Id,
    string Url,
    string? ThumbnailUrl,
    string? ProductCardUrl,
    string? ProductDetailUrl,
    string? GalleryUrl,
    string? AltText,
    string? Caption,
    bool IsMain,
    int DisplayOrder
);

/// <summary>
/// Server-side add-to-cart request. The browser may only supply the product, the
/// selected variant and the quantity; every other field is recomputed on the server.
/// </summary>
public sealed record AddToCartRequest(
    Guid ProductId,
    Guid VariantId,
    int Quantity
);

/// <summary>
/// Result of the server-side add-to-cart validation. Pricing, SKU, image and stock
/// are resolved from the database so the client never dictates the values.
/// </summary>
public sealed record AddToCartResult(
    bool Success,
    string? ErrorMessage,
    AddToCartItemDto? Item
);

/// <summary>
/// Server-computed line item returned to the client after a validated add-to-cart.
/// </summary>
public sealed record AddToCartItemDto(
    Guid ProductId,
    Guid VariantId,
    string ProductName,
    string VariantSku,
    string? ImageUrl,
    string? ColourName,
    string? SizeName,
    decimal UnitPrice,
    decimal? CompareAtPrice,
    int Quantity,
    decimal LineTotal,
    int AvailableStock
);
