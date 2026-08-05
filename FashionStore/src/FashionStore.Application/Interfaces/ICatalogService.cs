using FashionStore.Application.DTOs.Catalog;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Storefront catalogue search and listing. This interface is the abstraction seam
/// for a future external search engine; the default implementation runs indexed
/// SQL-friendly queries against the relational store.
/// </summary>
public interface ICatalogService
{
    Task<CatalogPageData> GetProductsAsync(ProductListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the display name for a catalogue entity (category, brand or collection)
    /// by slug, or returns null when the entity is unknown or inactive.
    /// </summary>
    Task<string?> ResolveEntityNameAsync(CatalogEntityKind kind, string slug, CancellationToken cancellationToken = default);
}
