using System.Linq.Expressions;
using System.Text.Json;
using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Home;
using FashionStore.Application.DTOs.Navigation;
using FashionStore.Application.Interfaces;
using FashionStore.Application.Services;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Composes the public storefront homepage from configuration-driven sections
/// and live catalogue data. Results are cached under a single aggregate key and
/// invalidated by catalogue, inventory and image write paths.
/// </summary>
public sealed class HomePageService : IHomePageService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly IFileStorageService _storage;
    private readonly INavigationService _navigation;
    private readonly HomePageSettings _settings;
    private readonly CacheSettings _cacheSettings;
    private readonly ILogger<HomePageService> _logger;

    public HomePageService(
        AppDbContext context,
        IDistributedCache cache,
        IFileStorageService storage,
        INavigationService navigation,
        HomePageSettings settings,
        CacheSettings cacheSettings,
        ILogger<HomePageService> logger)
    {
        _context = context;
        _cache = cache;
        _storage = storage;
        _navigation = navigation;
        _settings = settings;
        _cacheSettings = cacheSettings;
        _logger = logger;
    }

    public async Task<HomePageData> GetHomePageAsync(CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.HomePage, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<HomePageData>(cached)!;
        }

        var data = await BuildHomePageDataAsync(cancellationToken);
        await _cache.SetStringAsync(CacheKeys.HomePage, JsonSerializer.Serialize(data), GetCacheOptions(), cancellationToken);
        return data;
    }

    public async Task InvalidateHomePageCacheAsync(CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(CacheKeys.HomePage, cancellationToken);
    }

    private async Task<HomePageData> BuildHomePageDataAsync(CancellationToken cancellationToken)
    {
        var announcements = _settings.EnableAnnouncementBar
            ? _navigation.GetActiveAnnouncements().ToList()
            : new List<Announcement>();

        var hero = new HeroBannerDto(
            _settings.Hero.Title,
            _settings.Hero.Subtitle,
            _settings.Hero.ImageUrl,
            _settings.Hero.CtaText,
            _settings.Hero.CtaUrl);

        var promoBanners = _settings.PromoBanners
            .Select(b => new PromoBannerDto(b.Title, b.Subtitle, b.ImageUrl, b.LinkText, b.LinkUrl, b.Style))
            .ToList();

        var benefits = _settings.Benefits
            .Take(Math.Max(0, _settings.BenefitCount))
            .Select(b => new BenefitDto(b.Icon, b.Title, b.Description))
            .ToList();

        var lookbook = BuildLookbook();

        var categories = _settings.EnableCategories
            ? await GetCategoriesAsync(cancellationToken)
            : new List<HomeCategoryDto>();

        var newArrivals = _settings.EnableNewArrivals
            ? await GetProductCardsAsync(p => p.IsNewArrival, p => p.CreatedAtUtc, false, _settings.NewArrivalsCount, cancellationToken)
            : new List<HomeProductCardDto>();

        var featuredProducts = _settings.EnableFeaturedProducts
            ? await GetProductCardsAsync(p => p.IsFeatured, p => p.DisplayOrder, true, _settings.FeaturedProductsCount, cancellationToken)
            : new List<HomeProductCardDto>();

        var bestSellers = _settings.EnableBestSellers
            ? await GetProductCardsAsync(p => p.IsBestSeller, p => p.DisplayOrder, true, _settings.BestSellersCount, cancellationToken)
            : new List<HomeProductCardDto>();

        var saleProducts = _settings.EnableSaleProducts
            ? await GetProductCardsAsync(
                p => p.CompareAtPrice.HasValue && p.CompareAtPrice.Value > p.BasePrice,
                p => p.CompareAtPrice ?? 0m,
                false,
                _settings.SaleProductsCount,
                cancellationToken)
            : new List<HomeProductCardDto>();

        var collections = _settings.EnableCollections
            ? await GetCollectionsAsync(cancellationToken)
            : new List<HomeCollectionDto>();

        var brands = _settings.EnableBrands
            ? await GetBrandsAsync(cancellationToken)
            : new List<HomeBrandDto>();

        return new HomePageData(
            announcements,
            hero,
            promoBanners,
            categories,
            newArrivals,
            featuredProducts,
            bestSellers,
            collections,
            saleProducts,
            brands,
            benefits,
            lookbook,
            _settings.EnableNewsletter);
    }

    private LookbookDto? BuildLookbook()
    {
        var lookbook = _settings.Lookbook;
        if (lookbook == null || string.IsNullOrWhiteSpace(lookbook.Title))
        {
            return null;
        }

        return new LookbookDto(
            lookbook.Title,
            lookbook.Subtitle,
            lookbook.ImageUrl,
            lookbook.LinkText,
            lookbook.LinkUrl);
    }

    private async Task<List<HomeCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var count = Math.Max(0, _settings.CategoryCount);
        if (count == 0)
        {
            return new List<HomeCategoryDto>();
        }

        var productCounts = await _context.Set<Product>()
            .AsNoTracking()
            .Where(p => p.IsActive)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ParentCategoryId == null)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Take(count)
            .Select(c => new HomeCategoryDto(
                c.Id,
                c.Name,
                c.Slug,
                c.ImageUrl,
                c.IconUrl,
                0))
            .ToListAsync(cancellationToken);

        for (int i = 0; i < categories.Count; i++)
        {
            if (productCounts.TryGetValue(categories[i].Id, out var productCount))
            {
                categories[i] = categories[i] with { ProductCount = productCount };
            }
        }

        return categories;
    }

    private async Task<List<HomeProductCardDto>> GetProductCardsAsync(
        Expression<Func<Product, bool>> sectionFilter,
        Expression<Func<Product, object>> orderBy,
        bool ascending,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return new List<HomeProductCardDto>();
        }

        var now = DateTime.UtcNow;

        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.PublishedAtUtc != null && p.PublishedAtUtc <= now)
            .Where(sectionFilter);

        query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);

        var products = await query
            .Take(count)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                p.BasePrice,
                p.CompareAtPrice,
                p.IsNewArrival,
                p.CreatedAtUtc,
                ImageFileName = p.Images
                    .OrderBy(i => i.IsMain ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.FileName)
                    .FirstOrDefault(),
                ImageAltText = p.Images
                    .OrderBy(i => i.IsMain ? 0 : 1)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.AltText)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            return new List<HomeProductCardDto>();
        }

        var productIds = products.Select(p => p.Id).ToList();
        var colourMap = await CatalogQueryHelpers.GetColourMapAsync(_context, productIds, cancellationToken);
        var stockMap = await CatalogQueryHelpers.GetStockMapAsync(_context, productIds, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        return products.Select(p =>
        {
            var colours = colourMap.TryGetValue(p.Id, out var productColours)
                ? productColours
                : new List<HomeColourDto>();
            var stock = stockMap.TryGetValue(p.Id, out var available) ? available : (int?)null;
            var isNew = CatalogQueryHelpers.IsRecentlyCreated(p.CreatedAtUtc, p.IsNewArrival, nowUtc);

            return new HomeProductCardDto(
                p.Id,
                p.Name,
                p.Slug,
                p.BrandName,
                CatalogQueryHelpers.ResolveImageUrl(_storage, p.ImageFileName),
                CatalogQueryHelpers.ResolveCardImageUrl(_storage, p.ImageFileName),
                p.ImageAltText,
                p.BasePrice,
                p.CompareAtPrice,
                CatalogQueryHelpers.CalculateDiscountPercent(p.BasePrice, p.CompareAtPrice),
                isNew,
                stock.HasValue && stock.Value > 0,
                stock.HasValue && stock.Value > 0 && CatalogQueryHelpers.IsLowStock(stock.Value),
                colours);
        }).ToList();
    }

    private async Task<List<HomeCollectionDto>> GetCollectionsAsync(CancellationToken cancellationToken)
    {
        var count = Math.Max(0, _settings.CollectionCount);
        if (count == 0)
        {
            return new List<HomeCollectionDto>();
        }

        var now = DateTime.UtcNow;

        return await _context.Collections
            .AsNoTracking()
            .Where(c =>
                c.IsActive &&
                (c.StartAtUtc == null || c.StartAtUtc <= now) &&
                (c.EndAtUtc == null || c.EndAtUtc > now))
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Take(count)
            .Select(c => new HomeCollectionDto(
                c.Id,
                c.Name,
                c.Slug,
                c.BannerImageUrl,
                c.Description,
                c.EndAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<HomeBrandDto>> GetBrandsAsync(CancellationToken cancellationToken)
    {
        var count = Math.Max(0, _settings.BrandCount);
        if (count == 0)
        {
            return new List<HomeBrandDto>();
        }

        return await _context.Brands
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .Take(count)
            .Select(b => new HomeBrandDto(
                b.Id,
                b.Name,
                b.Slug,
                b.LogoUrl))
            .ToListAsync(cancellationToken);
    }

    private DistributedCacheEntryOptions GetCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes),
        SlidingExpiration = TimeSpan.FromMinutes(_cacheSettings.SlidingExpirationMinutes)
    };
}
