namespace FashionStore.Application.DTOs.Seo;

/// <summary>
/// A single sitemap URL entry with the metadata search engines use to crawl the
/// storefront efficiently. LastModified, ChangeFrequency and Priority follow the
/// sitemaps.org protocol.
/// </summary>
public sealed record SitemapEntry(
    string Url,
    DateTime? LastModifiedUtc = null,
    string? ChangeFrequency = null,
    double? Priority = null);

/// <summary>
/// The full sitemap payload returned by the sitemap service. Entries are grouped
/// by section so the controller can honour per-section change frequency defaults.
/// </summary>
public sealed record SitemapData(
    string SiteUrl,
    IReadOnlyList<SitemapEntry> Entries);
