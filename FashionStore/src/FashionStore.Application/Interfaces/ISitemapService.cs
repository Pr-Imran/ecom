using FashionStore.Application.DTOs.Seo;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Builds the storefront XML sitemap. Only live, indexable content is included
/// (published pages, active categories/brands/collections and purchasable
/// products) using projected, no-tracking queries so the sitemap never loads
/// full entities into memory. The result is cached and invalidated whenever
/// catalogue or content caches are invalidated.
/// </summary>
public interface ISitemapService
{
    Task<SitemapData> GetSitemapAsync(string siteUrl, CancellationToken cancellationToken = default);
}
