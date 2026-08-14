using FashionStore.Application.Configuration;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class SitemapServiceTests
{
    private readonly IDistributedCache _cache = new MemoryDistributedCache(
        new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-sitemap-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private SitemapService CreateService(AppDbContext context)
        => new(context, _cache, new CacheSettings { AbsoluteExpirationMinutes = 10 }, NullLogger<SitemapService>.Instance);

    [Fact]
    public async Task GetSitemap_IncludesStaticPublicRoutes()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var sitemap = await service.GetSitemapAsync("https://example.com");

        Assert.Contains(sitemap.Entries, e => e.Url == "/");
        Assert.Contains(sitemap.Entries, e => e.Url == "/products");
        Assert.Contains(sitemap.Entries, e => e.Url == "/privacy-policy");
    }

    [Fact]
    public async Task GetSitemap_IncludesActivePublishedProductsOnly()
    {
        var context = CreateContext();
        await SeedProductAsync(context, "active-product", isActive: true, published: true);
        await SeedProductAsync(context, "inactive-product", isActive: false, published: true);
        await SeedProductAsync(context, "unpublished-product", isActive: true, published: false);
        var service = CreateService(context);

        var sitemap = await service.GetSitemapAsync("https://example.com");

        Assert.Contains(sitemap.Entries, e => e.Url == "/products/active-product");
        Assert.DoesNotContain(sitemap.Entries, e => e.Url == "/products/inactive-product");
        Assert.DoesNotContain(sitemap.Entries, e => e.Url == "/products/unpublished-product");
    }

    [Fact]
    public async Task GetSitemap_IncludesActiveCatalogueEntities()
    {
        var context = CreateContext();
        context.Categories.Add(new Category { Name = "Outerwear", Slug = "outerwear", IsActive = true });
        context.Brands.Add(new Brand { Name = "Northbrand", Slug = "northbrand", IsActive = true });
        context.Collections.Add(new Collection { Name = "Winter 2026", Slug = "winter-2026", IsActive = true });
        context.Brands.Add(new Brand { Name = "Hidden", Slug = "hidden", IsActive = false });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var sitemap = await service.GetSitemapAsync("https://example.com");

        Assert.Contains(sitemap.Entries, e => e.Url == "/categories/outerwear");
        Assert.Contains(sitemap.Entries, e => e.Url == "/brands/northbrand");
        Assert.Contains(sitemap.Entries, e => e.Url == "/collections/winter-2026");
        Assert.DoesNotContain(sitemap.Entries, e => e.Url == "/brands/hidden");
    }

    [Fact]
    public async Task GetSitemap_IncludesPublishedSystemPageAtRootRoute()
    {
        var context = CreateContext();
        context.ContentPages.Add(SeedPage("about", isSystem: true, published: true));
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var sitemap = await service.GetSitemapAsync("https://example.com");

        Assert.Contains(sitemap.Entries, e => e.Url == "/about");
        Assert.DoesNotContain(sitemap.Entries, e => e.Url == "/pages/about");
    }

    [Fact]
    public async Task GetSitemap_CookiePolicyPageUsesPagesRoute()
    {
        var context = CreateContext();
        context.ContentPages.Add(SeedPage("cookie-policy", isSystem: true, published: true));
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var sitemap = await service.GetSitemapAsync("https://example.com");

        Assert.Contains(sitemap.Entries, e => e.Url == "/pages/cookie-policy");
        Assert.DoesNotContain(sitemap.Entries, e => e.Url == "/cookie-policy");
    }

    [Fact]
    public async Task GetSitemap_ExcludesDraftPages()
    {
        var context = CreateContext();
        context.ContentPages.Add(SeedPage("draft-page", isSystem: false, published: false));
        context.ContentPages.Add(SeedPage("live-page", isSystem: false, published: true));
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var sitemap = await service.GetSitemapAsync("https://example.com");

        Assert.DoesNotContain(sitemap.Entries, e => e.Url == "/pages/draft-page");
        Assert.Contains(sitemap.Entries, e => e.Url == "/pages/live-page");
    }

    private static ContentPage SeedPage(string slug, bool isSystem, bool published)
    {
        var now = DateTime.UtcNow;
        return new ContentPage
        {
            Title = slug,
            Slug = slug,
            Summary = null,
            BodyHtml = null,
            Template = ContentPageTemplate.Default,
            Status = published ? ContentStatus.Published : ContentStatus.Draft,
            IsSystem = isSystem,
            PublishedAtUtc = published ? now : null,
            MetaTitle = null,
            MetaDescription = null,
            CreatedAtUtc = now,
            CreatedBy = "test"
        };
    }

    private static async Task SeedProductAsync(AppDbContext context, string slug, bool isActive, bool published)
    {
        var now = DateTime.UtcNow;
        context.Products.Add(new Product
        {
            Name = slug,
            Slug = slug,
            ShortDescription = null,
            FullDescription = null,
            IsActive = isActive,
            PublishedAtUtc = published ? now : null,
            BasePrice = 100m,
            CompareAtPrice = null,
            BaseSku = slug.ToUpperInvariant(),
            CreatedAtUtc = now,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
    }
}
