using System.Text.Json;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ProductService> _logger;
    private readonly CacheSettings _cacheSettings;

    public ProductService(
        AppDbContext context,
        IDistributedCache cache,
        ILogger<ProductService> logger,
        CacheSettings cacheSettings)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _cacheSettings = cacheSettings;
    }

    public async Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = $"product:{id}";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<ProductDto>(cached);

        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Collection)
            .Include(p => p.ProductTagMappings).ThenInclude(m => m.ProductTag)
            .Include(p => p.Specifications)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null) return null;

        var dto = ToDto(product);
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(dto), GetCacheOptions(), cancellationToken);
        return dto;
    }

    public async Task<ProductSearchResult> SearchAsync(ProductSearchRequest request, CancellationToken cancellationToken = default)
    {
#pragma warning disable CA1862
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLowerInvariant().Contains(term) ||
                (p.ShortDescription != null && p.ShortDescription.ToLowerInvariant().Contains(term)) ||
                (p.BaseSku != null && p.BaseSku.ToLowerInvariant().Contains(term)) ||
                (p.Barcode != null && p.Barcode.ToLowerInvariant().Contains(term)) ||
                (p.SearchKeywords != null && p.SearchKeywords.ToLowerInvariant().Contains(term)));
        }
#pragma warning restore CA1862

        if (request.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);

        if (request.BrandId.HasValue)
            query = query.Where(p => p.BrandId == request.BrandId.Value);

        if (request.IsActive.HasValue)
            query = query.Where(p => p.IsActive == request.IsActive.Value);

        if (request.IsFeatured.HasValue)
            query = query.Where(p => p.IsFeatured == request.IsFeatured.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        query = request.SortBy?.ToLowerInvariant() switch
        {
            "name" => request.SortDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "price" => request.SortDescending ? query.OrderByDescending(p => p.BasePrice) : query.OrderBy(p => p.BasePrice),
            "newest" => request.SortDescending ? query.OrderByDescending(p => p.CreatedAtUtc) : query.OrderBy(p => p.CreatedAtUtc),
            "published" => request.SortDescending ? query.OrderByDescending(p => p.PublishedAtUtc) : query.OrderBy(p => p.PublishedAtUtc),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };

        var products = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new ProductListDto(
                p.Id,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.BaseSku,
                p.BasePrice,
                p.IsActive,
                p.IsFeatured,
                p.PublishedAtUtc,
                p.Category!.Name,
                p.Brand != null ? p.Brand.Name : null,
                p.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new ProductSearchResult(
            products,
            totalCount,
            request.Page,
            request.PageSize,
            totalPages
        );
    }

    public async Task<IEnumerable<ProductListDto>> GetFeaturedProductsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var key = "products:featured";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<IEnumerable<ProductListDto>>(cached)!;

        var products = await _context.Products
            .Where(p => p.IsActive && p.IsFeatured && p.PublishedAtUtc <= DateTime.UtcNow)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .OrderBy(p => p.DisplayOrder)
            .Take(count)
            .Select(p => new ProductListDto(
                p.Id,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.BaseSku,
                p.BasePrice,
                p.IsActive,
                p.IsFeatured,
                p.PublishedAtUtc,
                p.Category!.Name,
                p.Brand != null ? p.Brand.Name : null,
                p.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(products), GetCacheOptions(), cancellationToken);
        return products;
    }

    public async Task<IEnumerable<ProductListDto>> GetNewArrivalsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Where(p => p.IsActive && p.IsNewArrival && p.PublishedAtUtc <= DateTime.UtcNow)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(count)
            .Select(p => new ProductListDto(
                p.Id,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.BaseSku,
                p.BasePrice,
                p.IsActive,
                p.IsFeatured,
                p.PublishedAtUtc,
                p.Category!.Name,
                p.Brand != null ? p.Brand.Name : null,
                p.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return products;
    }

    public async Task<IEnumerable<ProductListDto>> GetBestSellersAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Where(p => p.IsActive && p.IsBestSeller && p.PublishedAtUtc <= DateTime.UtcNow)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .OrderBy(p => p.DisplayOrder)
            .Take(count)
            .Select(p => new ProductListDto(
                p.Id,
                p.Name,
                p.Slug,
                p.ShortDescription,
                p.BaseSku,
                p.BasePrice,
                p.IsActive,
                p.IsFeatured,
                p.PublishedAtUtc,
                p.Category!.Name,
                p.Brand != null ? p.Brand.Name : null,
                p.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return products;
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var slug = GenerateSlug(request.Name);
        if (!await IsSlugUniqueAsync(slug, null, cancellationToken))
            throw new InvalidOperationException($"Product with slug '{slug}' already exists");

        ValidatePrice(request.BasePrice, request.CompareAtPrice, request.CostPrice);

        var product = new Product
        {
            Name = request.Name,
            Slug = slug,
            ShortDescription = request.ShortDescription,
            FullDescription = SanitizeHtml(request.FullDescription),
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            CollectionId = request.CollectionId,
            ProductType = request.ProductType,
            Material = request.Material,
            Fabric = request.Fabric,
            CareInstructions = request.CareInstructions,
            Gender = request.Gender,
            CountryOfOrigin = request.CountryOfOrigin,
            BaseSku = request.BaseSku,
            Barcode = request.Barcode,
            BasePrice = request.BasePrice,
            CompareAtPrice = request.CompareAtPrice,
            CostPrice = request.CostPrice,
            TaxCategory = request.TaxCategory,
            Weight = request.Weight,
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured,
            IsNewArrival = request.IsNewArrival,
            IsBestSeller = request.IsBestSeller,
            AllowReviews = request.AllowReviews,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription,
            SearchKeywords = request.SearchKeywords
        };

        if (request.PublishedAtUtc.HasValue)
        {
            product.PublishedAtUtc = request.PublishedAtUtc;
        }
        else if (product.IsActive)
        {
            product.PublishedAtUtc = DateTime.UtcNow;
        }

        _context.Products.Add(product);

        if (request.TagIds != null && request.TagIds.Count > 0)
        {
            foreach (var tagId in request.TagIds)
            {
                product.ProductTagMappings.Add(new ProductTagMapping
                {
                    ProductId = product.Id,
                    ProductTagId = tagId
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(product.Id, cancellationToken);

        _logger.LogInformation("Created product {ProductId} - {Name}", product.Id, product.Name);
        return ToDto(product);
    }

    public async Task<ProductDto?> UpdateAsync(UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Include(p => p.ProductTagMappings)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product == null) return null;

        var slug = GenerateSlug(request.Name);
        if (!await IsSlugUniqueAsync(slug, request.Id, cancellationToken))
            throw new InvalidOperationException($"Product with slug '{slug}' already exists");

        ValidatePrice(request.BasePrice, request.CompareAtPrice, request.CostPrice);

        product.Name = request.Name;
        product.Slug = slug;
        product.ShortDescription = request.ShortDescription;
        product.FullDescription = SanitizeHtml(request.FullDescription);
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.CollectionId = request.CollectionId;
        product.ProductType = request.ProductType;
        product.Material = request.Material;
        product.Fabric = request.Fabric;
        product.CareInstructions = request.CareInstructions;
        product.Gender = request.Gender;
        product.CountryOfOrigin = request.CountryOfOrigin;
        product.BaseSku = request.BaseSku;
        product.Barcode = request.Barcode;
        product.BasePrice = request.BasePrice;
        product.CompareAtPrice = request.CompareAtPrice;
        product.CostPrice = request.CostPrice;
        product.TaxCategory = request.TaxCategory;
        product.Weight = request.Weight;
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.IsNewArrival = request.IsNewArrival;
        product.IsBestSeller = request.IsBestSeller;
        product.AllowReviews = request.AllowReviews;
        product.SeoTitle = request.SeoTitle;
        product.SeoDescription = request.SeoDescription;
        product.SearchKeywords = request.SearchKeywords;

        if (request.PublishedAtUtc.HasValue)
        {
            product.PublishedAtUtc = request.PublishedAtUtc;
        }
        else if (product.IsActive && !product.PublishedAtUtc.HasValue)
        {
            product.PublishedAtUtc = DateTime.UtcNow;
        }

        if (request.TagIds != null)
        {
            product.ProductTagMappings.Clear();
            foreach (var tagId in request.TagIds)
            {
                product.ProductTagMappings.Add(new ProductTagMapping
                {
                    ProductId = product.Id,
                    ProductTagId = tagId
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(request.Id, cancellationToken);

        _logger.LogInformation("Updated product {ProductId}", request.Id);
        return ToDto(product);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product == null) return false;

        _context.ProductVariants.RemoveRange(product.Variants);
        _context.Products.Remove(product);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(id, cancellationToken);

        _logger.LogInformation("Deleted product {ProductId}", id);
        return true;
    }

    public async Task<ProductDto> DuplicateAsync(DuplicateProductRequest request, CancellationToken cancellationToken = default)
    {
        var sourceProduct = await _context.Products
            .Include(p => p.ProductTagMappings)
            .FirstOrDefaultAsync(p => p.Id == request.SourceProductId, cancellationToken);

        if (sourceProduct == null)
            throw new InvalidOperationException($"Product with ID {request.SourceProductId} not found");

        var newSku = !string.IsNullOrWhiteSpace(request.NewSku) ? request.NewSku : $"{sourceProduct.BaseSku}-COPY";

        if (!await IsSkuUniqueAsync(newSku, null, cancellationToken))
            throw new InvalidOperationException($"Product with SKU '{newSku}' already exists");

        var duplicate = new Product
        {
            Name = request.NewName,
            Slug = GenerateSlug(request.NewName),
            ShortDescription = sourceProduct.ShortDescription,
            FullDescription = sourceProduct.FullDescription,
            CategoryId = sourceProduct.CategoryId,
            BrandId = sourceProduct.BrandId,
            CollectionId = sourceProduct.CollectionId,
            ProductType = sourceProduct.ProductType,
            Material = sourceProduct.Material,
            Fabric = sourceProduct.Fabric,
            CareInstructions = sourceProduct.CareInstructions,
            Gender = sourceProduct.Gender,
            CountryOfOrigin = sourceProduct.CountryOfOrigin,
            BaseSku = newSku,
            Barcode = sourceProduct.Barcode,
            BasePrice = sourceProduct.BasePrice,
            CompareAtPrice = sourceProduct.CompareAtPrice,
            CostPrice = sourceProduct.CostPrice,
            TaxCategory = sourceProduct.TaxCategory,
            Weight = sourceProduct.Weight,
            IsActive = false,
            IsFeatured = false,
            IsNewArrival = false,
            IsBestSeller = false,
            AllowReviews = sourceProduct.AllowReviews,
            SeoTitle = sourceProduct.SeoTitle,
            SeoDescription = sourceProduct.SeoDescription,
            SearchKeywords = sourceProduct.SearchKeywords
        };

        foreach (var tagMapping in sourceProduct.ProductTagMappings)
        {
            duplicate.ProductTagMappings.Add(new ProductTagMapping
            {
                ProductId = duplicate.Id,
                ProductTagId = tagMapping.ProductTagId
            });
        }

        _context.Products.Add(duplicate);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Duplicated product {SourceId} to {NewId} - {Name}", request.SourceProductId, duplicate.Id, request.NewName);
        return ToDto(duplicate);
    }

    public async Task<bool> PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync(new object[] { id }, cancellationToken);
        if (product == null) return false;

        product.IsActive = true;
        product.PublishedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(id, cancellationToken);

        _logger.LogInformation("Published product {ProductId}", id);
        return true;
    }

    public async Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync(new object[] { id }, cancellationToken);
        if (product == null) return false;

        product.IsActive = false;
        product.IsFeatured = false;
        product.IsNewArrival = false;
        product.IsBestSeller = false;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(id, cancellationToken);

        _logger.LogInformation("Archived product {ProductId}", id);
        return true;
    }

    public async Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Products.Where(p => p.Slug == slug);
        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);
        return !await query.AnyAsync(cancellationToken);
    }

    private async Task<bool> IsSkuUniqueAsync(string sku, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Products.Where(p => p.BaseSku == sku);
        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);
        return !await query.AnyAsync(cancellationToken);
    }

    private ProductDto ToDto(Product product)
    {
        var tags = product.ProductTagMappings
            .Where(m => m.ProductTag != null)
            .Select(m => m.ProductTag!.Name)
            .ToList();

        return new ProductDto(
            product.Id,
            product.Name,
            product.Slug,
            product.ShortDescription,
            product.CategoryId,
            product.Category?.Name,
            product.BrandId,
            product.Brand?.Name,
            product.CollectionId,
            product.Collection?.Name,
            product.ProductType,
            product.Material,
            product.Fabric,
            product.CareInstructions,
            product.Gender,
            product.CountryOfOrigin,
            product.BaseSku,
            product.Barcode,
            product.BasePrice,
            product.CompareAtPrice,
            product.CostPrice,
            product.TaxCategory,
            product.Weight,
            product.IsActive,
            product.IsFeatured,
            product.IsNewArrival,
            product.IsBestSeller,
            product.AllowReviews,
            product.PublishedAtUtc,
            product.SeoTitle,
            product.SeoDescription,
            product.SearchKeywords,
            product.CreatedAtUtc,
            product.UpdatedAtUtc,
            tags
        );
    }

    private string GenerateSlug(string name) => name.ToLowerInvariant()
        .Replace(" ", "-")
        .Replace("--", "-")
        .Trim('-');

    private static void ValidatePrice(decimal basePrice, decimal? compareAtPrice, decimal? costPrice)
    {
        if (basePrice < 0)
            throw new InvalidOperationException("Base price cannot be negative");

        if (compareAtPrice.HasValue && compareAtPrice.Value < basePrice)
            throw new InvalidOperationException("Compare at price must be greater than or equal to base price");

        if (costPrice.HasValue && costPrice.Value < 0)
            throw new InvalidOperationException("Cost price cannot be negative");
    }

    private static string SanitizeHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var allowedTags = new[]
        {
            "p", "br", "strong", "em", "u", "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6",
            "blockquote", "code", "pre", "span", "div"
        };

        var sanitized = html;
        foreach (var tag in allowedTags)
        {
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                $"<(?!/?{tag}\\b)[^>]*>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            "<(\\w+)([^>]*)>",
            match =>
            {
                var tagName = match.Groups[1].Value.ToLowerInvariant();
                if (allowedTags.Contains(tagName))
                {
                    var attributes = match.Groups[2].Value;
                    attributes = System.Text.RegularExpressions.Regex.Replace(attributes, @"(on\w+)=[""'][^""']*[""']", string.Empty);
                    return $"<{tagName}{attributes}>";
                }
                return string.Empty;
            });

        return sanitized;
    }

    private async Task InvalidateCacheAsync(Guid? productId = null, CancellationToken cancellationToken = default)
    {
        if (productId.HasValue)
        {
            await _cache.RemoveAsync($"product:{productId}", cancellationToken);
        }

        await _cache.RemoveAsync("products:featured", cancellationToken);
        await _cache.RemoveAsync(Application.Common.CacheKeys.HomePage, cancellationToken);
        await _cache.RemoveAsync(Application.Common.CacheKeys.Sitemap, cancellationToken);
    }

    private DistributedCacheEntryOptions GetCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes)
    };
}
