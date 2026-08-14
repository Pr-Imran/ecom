using System.Text.Json;
using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Seo;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Builds the storefront XML sitemap from projected, no-tracking queries. Only
/// live, indexable content is included: purchasable products, active catalogue
/// entities, published content pages and the static public routes. The entry
/// list is cached (relative URLs) so repeated crawls do not hit the database,
/// and it is invalidated whenever catalogue or content caches are invalidated.
/// </summary>
public sealed class SitemapService : ISitemapService
{
    private static readonly SitemapEntry[] StaticEntries =
    {
        new("/", null, "daily", 1.0),
        new("/products", null, "daily", 0.9),
        new("/products/new", null, "daily", 0.8),
        new("/products/sale", null, "daily", 0.8),
        new("/products/best", null, "daily", 0.8),
        new("/about", null, "monthly", 0.5),
        new("/contact", null, "monthly", 0.5),
        new("/size-guide", null, "monthly", 0.4),
        new("/faq", null, "monthly", 0.5),
        new("/delivery-policy", null, "yearly", 0.3),
        new("/return-policy", null, "yearly", 0.3),
        new("/privacy-policy", null, "yearly", 0.3),
        new("/terms", null, "yearly", 0.3)
    };

    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly CacheSettings _cacheSettings;
    private readonly ILogger<SitemapService> _logger;

    public SitemapService(
        AppDbContext context,
        IDistributedCache cache,
        CacheSettings cacheSettings,
        ILogger<SitemapService> logger)
    {
        _context = context;
        _cache = cache;
        _cacheSettings = cacheSettings;
        _logger = logger;
    }

    public async Task<SitemapData> GetSitemapAsync(string siteUrl, CancellationToken cancellationToken = default)
    {
        siteUrl = siteUrl.TrimEnd('/');

        var cached = await _cache.GetStringAsync(CacheKeys.Sitemap, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            var entries = JsonSerializer.Deserialize<SitemapEntry[]>(cached) ?? [];
            return new SitemapData(siteUrl, entries);
        }

        var now = DateTime.UtcNow;

        try
        {
            var entries = await BuildEntriesAsync(now, cancellationToken);

            await _cache.SetStringAsync(
                CacheKeys.Sitemap,
                JsonSerializer.Serialize(entries),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, _cacheSettings.AbsoluteExpirationMinutes / 60.0 * 2))
                },
                cancellationToken);

            return new SitemapData(siteUrl, entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build the XML sitemap; returning static entries only.");
            return new SitemapData(siteUrl, StaticEntries);
        }
    }

    private async Task<IReadOnlyList<SitemapEntry>> BuildEntriesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var entries = new List<SitemapEntry>(StaticEntries);

        var products = await _context.Products.AsNoTracking()
            .Where(p => p.IsActive && p.PublishedAtUtc.HasValue && p.PublishedAtUtc.Value <= now)
            .Select(p => new SitemapEntry(
                "/products/" + p.Slug,
                p.UpdatedAtUtc ?? p.CreatedAtUtc,
                "weekly",
                0.8))
            .ToListAsync(cancellationToken);
        entries.AddRange(products);

        var categories = await _context.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new SitemapEntry(
                "/categories/" + c.Slug,
                c.UpdatedAtUtc ?? c.CreatedAtUtc,
                "weekly",
                0.7))
            .ToListAsync(cancellationToken);
        entries.AddRange(categories);

        var collections = await _context.Collections.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new SitemapEntry(
                "/collections/" + c.Slug,
                c.UpdatedAtUtc ?? c.CreatedAtUtc,
                "weekly",
                0.6))
            .ToListAsync(cancellationToken);
        entries.AddRange(collections);

        var brands = await _context.Brands.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new SitemapEntry(
                "/brands/" + b.Slug,
                b.UpdatedAtUtc ?? b.CreatedAtUtc,
                "monthly",
                0.5))
            .ToListAsync(cancellationToken);
        entries.AddRange(brands);

        var pages = await _context.ContentPages.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published && p.PublishedAtUtc.HasValue && p.PublishedAtUtc.Value <= now)
            .Select(p => new SitemapEntry(
                p.IsSystem && IsSystemSlug(p.Slug) ? "/" + p.Slug : "/pages/" + p.Slug,
                p.UpdatedAtUtc ?? p.PublishedAtUtc,
                "monthly",
                0.5))
            .ToListAsync(cancellationToken);
        entries.AddRange(pages);

        return entries
            .GroupBy(e => e.Url)
            .Select(g => g.First())
            .OrderBy(e => e.Url)
            .ToList();
    }

    private static bool IsSystemSlug(string slug)
        => slug is "about" or "contact" or "size-guide";
}
