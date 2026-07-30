using System.Text.Json;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

public class BrandService : IBrandService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<BrandService> _logger;
    private readonly CacheSettings _cacheSettings;

    public BrandService(AppDbContext context, IDistributedCache cache, ILogger<BrandService> logger, CacheSettings cacheSettings)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _cacheSettings = cacheSettings;
    }

    public async Task<BrandDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = $"brand:{id}";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<BrandDto>(cached);

        var brand = await _context.Brands.FindAsync(new object[] { id }, cancellationToken);
        if (brand == null) return null;

        var dto = ToDto(brand, await CountProducts(brand.Id, cancellationToken));
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(dto), GetCacheOptions(), cancellationToken);
        return dto;
    }

    public async Task<IEnumerable<BrandDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var brands = await FilterBrands(includeInactive)
            .OrderBy(b => b.DisplayOrder).ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);

        return await MapToDtos(brands, cancellationToken);
    }

    public async Task<IEnumerable<BrandDto>> GetActiveBrandsAsync(CancellationToken cancellationToken = default)
    {
        var key = "brands:active";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<IEnumerable<BrandDto>>(cached)!;

        var brands = await FilterBrands(includeInactive: false)
            .OrderBy(b => b.DisplayOrder).ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var dtos = await MapToDtos(brands, cancellationToken);
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(dtos), GetCacheOptions(), cancellationToken);
        return dtos;
    }

    public async Task<BrandDto> CreateAsync(CreateBrandRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSlugUniqueAsync(GenerateSlug(request.Name), null, cancellationToken))
            throw new InvalidOperationException($"Brand with slug '{GenerateSlug(request.Name)}' already exists");

        var brand = new Brand
        {
            Name = request.Name,
            Slug = GenerateSlug(request.Name),
            Description = request.Description,
            LogoUrl = request.LogoUrl,
            WebsiteUrl = request.WebsiteUrl,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription
        };

        _context.Brands.Add(brand);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Created brand {BrandId} - {Name}", brand.Id, brand.Name);
        return ToDto(brand, 0);
    }

    public async Task<BrandDto?> UpdateAsync(UpdateBrandRequest request, CancellationToken cancellationToken = default)
    {
        var brand = await _context.Brands.FindAsync(new object[] { request.Id }, cancellationToken);
        if (brand == null) return null;

        if (!await IsSlugUniqueAsync(GenerateSlug(request.Name), request.Id, cancellationToken))
            throw new InvalidOperationException($"Brand with slug '{GenerateSlug(request.Name)}' already exists");

        brand.Name = request.Name;
        brand.Slug = GenerateSlug(request.Name);
        brand.Description = request.Description;
        brand.LogoUrl = request.LogoUrl;
        brand.WebsiteUrl = request.WebsiteUrl;
        brand.DisplayOrder = request.DisplayOrder;
        brand.IsActive = request.IsActive;
        brand.SeoTitle = request.SeoTitle;
        brand.SeoDescription = request.SeoDescription;
        brand.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Updated brand {BrandId}", request.Id);
        return ToDto(brand, await CountProducts(brand.Id, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var brand = await _context.Brands.FindAsync(new object[] { id }, cancellationToken);
        if (brand == null) return false;

        if (await HasProductsAsync(id, cancellationToken))
            throw new InvalidOperationException("Cannot delete brand that has products");

        var idStr = id.ToString();
        _context.Brands.Remove(brand);
        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync($"brand:{idStr}", cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Deleted brand {BrandId}", id);
        return true;
    }

    public async Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Brands.Where(b => b.Slug == slug);
        if (excludeId.HasValue) query = query.Where(b => b.Id != excludeId);
        return !await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> HasProductsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Product>().AnyAsync(p => p.BrandId == id, cancellationToken);
    }

    public async Task<IEnumerable<BrandDto>> SearchAsync(string searchTerm, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = FilterBrands(includeInactive);
        var results = await query.Where(b => b.Name.Contains(searchTerm) || (b.Description != null && b.Description.Contains(searchTerm)))
            .OrderBy(b => b.Name).ToListAsync(cancellationToken);
        return await MapToDtos(results, cancellationToken);
    }

    public async Task ReorderAsync(IEnumerable<(Guid Id, int Order)> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var brand = await _context.Brands.FindAsync(new object[] { item.Id }, cancellationToken);
            if (brand != null)
            {
                brand.DisplayOrder = item.Order;
                brand.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
    }

    private IQueryable<Brand> FilterBrands(bool includeInactive)
    {
        var query = _context.Brands.AsNoTracking();
        return includeInactive ? query : query.Where(b => b.IsActive);
    }

    private async Task<IEnumerable<BrandDto>> MapToDtos(IEnumerable<Brand> brands, CancellationToken cancellationToken)
    {
        var productCounts = await _context.Set<Product>()
            .Where(p => p.BrandId.HasValue)
            .GroupBy(p => p.BrandId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id!.Value, x => x.Count, cancellationToken);

        return brands.Select(b => ToDto(b, productCounts.TryGetValue(b.Id, out var c) ? c : 0));
    }

    private BrandDto ToDto(Brand brand, int productCount) => new(
        brand.Id, brand.Name, brand.Slug, brand.DisplayOrder, brand.Description, brand.LogoUrl, brand.WebsiteUrl,
        brand.IsActive, brand.SeoTitle, brand.SeoDescription,
        brand.CreatedAtUtc, brand.UpdatedAtUtc, productCount
    );

    private async Task<int> CountProducts(Guid brandId, CancellationToken ct) =>
        await _context.Set<Product>().CountAsync(p => p.BrandId == brandId, ct);

    private string GenerateSlug(string name) => name.ToLowerInvariant().Replace(" ", "-").Replace("--", "-").Trim('-');
    private async Task InvalidateCacheAsync(CancellationToken ct) => await _cache.RemoveAsync("brands:active", ct);
    private DistributedCacheEntryOptions GetCacheOptions() => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes) };
}

public class CollectionService : ICollectionService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CollectionService> _logger;
    private readonly CacheSettings _cacheSettings;

    public CollectionService(AppDbContext context, IDistributedCache cache, ILogger<CollectionService> logger, CacheSettings cacheSettings)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _cacheSettings = cacheSettings;
    }

    public async Task<CollectionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = $"collection:{id}";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached)) return JsonSerializer.Deserialize<CollectionDto>(cached);

        var collection = await _context.Collections.FindAsync(new object[] { id }, cancellationToken);
        if (collection == null) return null;

        var dto = ToDto(collection, await CountProducts(collection.Id, cancellationToken));
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(dto), GetCacheOptions(), cancellationToken);
        return dto;
    }

    public async Task<IEnumerable<CollectionDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var collections = FilterCollections(includeInactive);
        var results = await collections.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        return await MapToDtos(results, cancellationToken);
    }

    public async Task<IEnumerable<CollectionDto>> GetActiveCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var key = "collections:active";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached)) return JsonSerializer.Deserialize<IEnumerable<CollectionDto>>(cached)!;

        var now = DateTime.UtcNow;
        var collections = await FilterCollections(includeInactive: false)
            .Where(c => (c.StartAtUtc == null || c.StartAtUtc <= now) && (c.EndAtUtc == null || c.EndAtUtc >= now))
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var dtos = await MapToDtos(collections, cancellationToken);
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(dtos), GetCacheOptions(), cancellationToken);
        return dtos;
    }

    public async Task<CollectionDto> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSlugUniqueAsync(GenerateSlug(request.Name), null, cancellationToken))
            throw new InvalidOperationException($"Collection with slug '{GenerateSlug(request.Name)}' already exists");

        var collection = new Collection
        {
            Name = request.Name,
            Slug = GenerateSlug(request.Name),
            Description = request.Description,
            BannerImageUrl = request.BannerImageUrl,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription
        };

        _context.Collections.Add(collection);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Created collection {CollectionId} - {Name}", collection.Id, collection.Name);
        return ToDto(collection, 0);
    }

    public async Task<CollectionDto?> UpdateAsync(UpdateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        var collection = await _context.Collections.FindAsync(new object[] { request.Id }, cancellationToken);
        if (collection == null) return null;

        if (!await IsSlugUniqueAsync(GenerateSlug(request.Name), request.Id, cancellationToken))
            throw new InvalidOperationException($"Collection with slug '{GenerateSlug(request.Name)}' already exists");

        collection.Name = request.Name;
        collection.Slug = GenerateSlug(request.Name);
        collection.Description = request.Description;
        collection.BannerImageUrl = request.BannerImageUrl;
        collection.StartAtUtc = request.StartAtUtc;
        collection.EndAtUtc = request.EndAtUtc;
        collection.DisplayOrder = request.DisplayOrder;
        collection.IsActive = request.IsActive;
        collection.SeoTitle = request.SeoTitle;
        collection.SeoDescription = request.SeoDescription;
        collection.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Updated collection {CollectionId}", request.Id);
        return ToDto(collection, await CountProducts(collection.Id, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var collection = await _context.Collections.FindAsync(new object[] { id }, cancellationToken);
        if (collection == null) return false;

        if (await HasProductsAsync(id, cancellationToken))
            throw new InvalidOperationException("Cannot delete collection that has products");

        _context.Collections.Remove(collection);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Deleted collection {CollectionId}", id);
        return true;
    }

    public async Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Collections.Where(c => c.Slug == slug);
        if (excludeId.HasValue) query = query.Where(c => c.Id != excludeId);
        return !await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> HasProductsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Product>().AnyAsync(p => p.CollectionId == id, cancellationToken);
    }

    public async Task<IEnumerable<CollectionDto>> SearchAsync(string searchTerm, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = FilterCollections(includeInactive);
        var results = await query.Where(c => c.Name.Contains(searchTerm) || (c.Description != null && c.Description.Contains(searchTerm)))
            .OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return await MapToDtos(results, cancellationToken);
    }

    public async Task ReorderAsync(IEnumerable<(Guid Id, int Order)> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var collection = await _context.Collections.FindAsync(new object[] { item.Id }, cancellationToken);
            if (collection != null) { collection.DisplayOrder = item.Order; collection.UpdatedAtUtc = DateTime.UtcNow; }
        }
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
    }

    private IQueryable<Collection> FilterCollections(bool includeInactive)
    {
        var query = _context.Collections.AsNoTracking();
        return includeInactive ? query : query.Where(c => c.IsActive);
    }

    private async Task<IEnumerable<CollectionDto>> MapToDtos(IEnumerable<Collection> collections, CancellationToken cancellationToken)
    {
        var counts = await _context.Set<Product>().Where(p => p.CollectionId.HasValue)
            .GroupBy(p => p.CollectionId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id!.Value, x => x.Count, cancellationToken);
        return collections.Select(c => ToDto(c, counts.TryGetValue(c.Id, out var ct) ? ct : 0));
    }

    private CollectionDto ToDto(Collection c, int productCount) => new(
        c.Id, c.Name, c.Slug, c.DisplayOrder, c.Description, c.BannerImageUrl, c.StartAtUtc, c.EndAtUtc, c.IsActive,
        c.SeoTitle, c.SeoDescription, c.CreatedAtUtc, c.UpdatedAtUtc, productCount
    );

    private Task<int> CountProducts(Guid id, CancellationToken ct) => _context.Set<Product>().CountAsync(p => p.CollectionId == id, ct);
    private string GenerateSlug(string name) => name.ToLowerInvariant().Replace(" ", "-").Replace("--", "-").Trim('-');
    private async Task InvalidateCacheAsync(CancellationToken ct) => await _cache.RemoveAsync("collections:active", ct);
    private DistributedCacheEntryOptions GetCacheOptions() => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes) };
}
