using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.DTOs.Products;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Composes the storefront product details page: product information, image gallery,
/// variation data, related products and the recently-viewed rail.
/// </summary>
public interface IProductDetailsService
{
    /// <summary>
    /// Loads the details aggregate for an active, published product by slug.
    /// <paramref name="recentlyViewedIds"/> are hydrated into the recently-viewed rail
    /// excluding the requested product. Returns null when the product is unknown,
    /// unpublished or inactive.
    /// </summary>
    Task<ProductDetailsData?> GetDetailsAsync(
        string slug,
        IReadOnlyList<Guid>? recentlyViewedIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Server-side validation for the add-to-cart action on the product details page.
/// The client only supplies the product, the variant and the quantity; all prices,
/// names and stock figures are recomputed from the database so browser-supplied
/// values are never trusted.
/// </summary>
public interface IAddToCartService
{
    Task<AddToCartResult> ValidateAsync(AddToCartRequest request, CancellationToken cancellationToken = default);
}
