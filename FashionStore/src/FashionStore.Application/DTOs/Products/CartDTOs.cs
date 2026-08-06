namespace FashionStore.Application.DTOs.Products;

/// <summary>
/// A single cart line combining the stored reference (product, variant, quantity)
/// with live catalogue data. Prices, names, stock and option values are always
/// server-computed on read; the client only ever submits identifiers and a
/// quantity, never trusted pricing.
/// </summary>
public sealed record CartItemDto(
    Guid CartItemId,
    Guid ProductId,
    Guid VariantId,
    string ProductName,
    string Slug,
    string? BrandName,
    string? ImageUrl,
    string? ImageCardUrl,
    string? ImageAltText,
    string Sku,
    string? ColourName,
    string? SizeName,
    decimal UnitPrice,
    decimal? CompareAtPrice,
    int? DiscountPercent,
    int Quantity,
    decimal LineTotal,
    int AvailableStock,
    bool IsAvailable,
    bool IsInStock,
    bool IsActive,
    string? UnavailableReason
);

/// <summary>
/// View model for the storefront cart page and mini-cart. Totals are recomputed
/// from live catalogue prices; unavailable items are still returned so the UI can
/// surface a clear warning while keeping them out of the payable subtotal.
/// </summary>
public sealed record CartViewData(
    IReadOnlyList<CartItemDto> Items,
    int ItemCount,
    decimal Subtotal,
    string FormattedSubtotal,
    bool IsAuthenticated,
    bool HasUnavailableItems
);

/// <summary>
/// Client-supplied cart mutation. Only identifiers and a quantity are accepted;
/// the server validates the product, the variant, available stock and the maximum
/// quantity and always recomputes pricing.
/// </summary>
public sealed record CartMutationRequest(
    Guid ProductId,
    Guid VariantId,
    int Quantity
);

/// <summary>
/// Result of a cart mutation carrying the updated item count and an optional
/// server-computed line item for the affected entry.
/// </summary>
public sealed record CartMutationResult(
    bool Success,
    string? ErrorMessage,
    int ItemCount,
    CartItemDto? Item = null
);

/// <summary>
/// Cookie-backed anonymous cart entry. Only identifiers and a quantity are stored;
/// all display and pricing data is resolved server-side when the cart is read.
/// </summary>
public sealed record AnonymousCartEntry(
    Guid ProductId,
    Guid VariantId,
    int Quantity
);
