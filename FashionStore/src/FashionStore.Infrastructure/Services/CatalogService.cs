using FashionStore.Application.DTOs.Catalog;
using FashionStore.Application.DTOs.Home;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Default <see cref="ICatalogService"/> implementation running indexed, SQL-friendly
/// queries against the relational store. All filters are applied on the server;
/// pagination, sorting and projections prevent N+1 queries.
/// </summary>
public sealed class CatalogService : ICatalogService
{
    private const string ColourAttributeName = "colour";
    private const string SizeAttributeName = "size";
    private const int DefaultPageSize = 24;
    private const int MaxPageSize = 48;

    private readonly AppDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(
        AppDbContext context,
        IFileStorageService storage,
        ILogger<CatalogService> logger)
    {
        _context = context;
        _storage = storage;
        _logger = logger;
    }

    public async Task<CatalogPageData> GetProductsAsync(ProductListQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        if (pageSize == 0)
        {
            pageSize = DefaultPageSize;
        }

        var filtered = ApplyFilters(BuildBaseQuery(), query, now);

        var sorted = ApplySort(filtered, ProductSortOptions.Parse(query.Sort));

        var totalCount = await filtered.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        if (page > totalPages && totalPages > 0)
        {
            page = totalPages;
        }

        var ids = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var items = ids.Count > 0
            ? await BuildItemsAsync(ids, now, cancellationToken)
            : new List<ProductListItemDto>();

        var facets = await BuildFacetsAsync(filtered, query, cancellationToken);
        var minMaxPrice = await GetPriceBoundsAsync(filtered, cancellationToken);

        return new CatalogPageData(
            new PagedResult<ProductListItemDto>(items, page, pageSize, totalCount, totalPages),
            facets,
            query,
            minMaxPrice.Min,
            minMaxPrice.Max,
            query.ListingTitle,
            query.ListingSubtitle,
            query.ListingLink);
    }

    public async Task<string?> ResolveEntityNameAsync(CatalogEntityKind kind, string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return kind switch
        {
            CatalogEntityKind.Category => await _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && c.Slug == normalized)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken),
            CatalogEntityKind.Brand => await _context.Brands
                .AsNoTracking()
                .Where(b => b.IsActive && b.Slug == normalized)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken),
            CatalogEntityKind.Collection => await _context.Collections
                .AsNoTracking()
                .Where(c => c.IsActive && c.Slug == normalized)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken),
            _ => null
        };
    }

    private IQueryable<Product> BuildBaseQuery()
    {
        var now = DateTime.UtcNow;
        return _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.PublishedAtUtc != null && p.PublishedAtUtc <= now);
    }

    private static IQueryable<Product> ApplyFilters(IQueryable<Product> query, ProductListQuery q, DateTime now)
    {
        var searchTerm = NormalizeSearchTerm(q.Q);
        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(p =>
                p.Name.Contains(searchTerm) ||
                p.BaseSku.Contains(searchTerm) ||
                (p.SearchKeywords != null && p.SearchKeywords.Contains(searchTerm)) ||
                (p.Brand != null && p.Brand.Name.Contains(searchTerm)) ||
                (p.Category != null && p.Category.Name.Contains(searchTerm)) ||
                p.ProductTagMappings.Any(m => m.ProductTag != null && m.ProductTag.Name.Contains(searchTerm)) ||
                p.Variants.Any(v => v.Sku.Contains(searchTerm)));
        }

        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            var slug = q.Category.Trim();
            query = query.Where(p => p.Category != null &&
                (p.Category.Slug == slug ||
                 (p.Category.ParentCategory != null && p.Category.ParentCategory.Slug == slug)));
        }

        if (!string.IsNullOrWhiteSpace(q.Brand))
        {
            var slug = q.Brand.Trim();
            query = query.Where(p => p.Brand != null && p.Brand.Slug == slug);
        }

        if (!string.IsNullOrWhiteSpace(q.Collection))
        {
            var slug = q.Collection.Trim();
            query = query.Where(p => p.Collection != null && p.Collection.Slug == slug);
        }

        var colours = CleanValues(q.Colour);
        if (colours.Length > 0)
        {
            query = query.Where(p => p.Variants.Any(v => v.IsActive &&
                v.VariantAttributeValues.Any(vav => vav.AttributeValue != null && colours.Contains(vav.AttributeValue.Slug))));
        }

        var sizes = CleanValues(q.Size);
        if (sizes.Length > 0)
        {
            query = query.Where(p => p.Variants.Any(v => v.IsActive &&
                v.VariantAttributeValues.Any(vav => vav.AttributeValue != null && sizes.Contains(vav.AttributeValue.Slug))));
        }

        var materials = CleanValues(q.Material);
        if (materials.Length > 0)
        {
            query = query.Where(p => p.Material != null && materials.Contains(p.Material));
        }

        var tags = CleanValues(q.Tag);
        if (tags.Length > 0)
        {
            query = query.Where(p => p.ProductTagMappings.Any(m => m.ProductTag != null && tags.Contains(m.ProductTag.Slug)));
        }

        if (!string.IsNullOrWhiteSpace(q.Gender))
        {
            var gender = q.Gender.Trim().ToLowerInvariant();
            query = query.Where(p => p.Gender != null && string.Equals(p.Gender, gender, StringComparison.OrdinalIgnoreCase));
        }

        var minPrice = q.MinPrice.HasValue ? Math.Max(0m, q.MinPrice.Value) : (decimal?)null;
        var maxPrice = q.MaxPrice.HasValue && q.MaxPrice.Value > 0 ? q.MaxPrice.Value : (decimal?)null;
        if (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice)
        {
            (minPrice, maxPrice) = (maxPrice, minPrice);
        }
        if (minPrice.HasValue)
        {
            query = query.Where(p => p.BasePrice >= minPrice.Value);
        }
        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.BasePrice <= maxPrice.Value);
        }

        if (q.InStock)
        {
            query = query.Where(p => p.Variants.Any(v => v.IsActive && (v.StockQuantity ?? 0) - (v.ReservedStock ?? 0) > 0));
        }

        if (q.OnSale || string.Equals(q.ListingType, "sale", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.CompareAtPrice.HasValue && p.CompareAtPrice.Value > p.BasePrice);
        }

        if (q.MinRating is >= 1 and <= 5)
        {
            var minRating = q.MinRating.Value;
            query = query.Where(p => p.Reviews.Any(r => r.IsApproved) &&
                p.Reviews.Where(r => r.IsApproved).Average(r => (double)r.Rating) >= minRating);
        }

        if (string.Equals(q.ListingType, "new", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.IsNewArrival);
        }

        if (string.Equals(q.ListingType, "best", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.IsBestSeller);
        }

        return query;
    }

    private static IQueryable<Product> ApplySort(IQueryable<Product> query, ProductSortOrder sort)
    {
        return sort switch
        {
            ProductSortOrder.Newest => query.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Name),
            ProductSortOrder.Oldest => query.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Name),
            ProductSortOrder.PriceLowHigh => query.OrderBy(p => p.BasePrice).ThenBy(p => p.Name),
            ProductSortOrder.PriceHighLow => query.OrderByDescending(p => p.BasePrice).ThenBy(p => p.Name),
            ProductSortOrder.Popularity => query.OrderBy(p => p.DisplayOrder).ThenByDescending(p => p.CreatedAtUtc),
            ProductSortOrder.BestSelling => query.OrderByDescending(p => p.IsBestSeller).ThenBy(p => p.DisplayOrder).ThenByDescending(p => p.CreatedAtUtc),
            ProductSortOrder.HighestRated => query
                .OrderByDescending(p => p.Reviews.Any(r => r.IsApproved)
                    ? p.Reviews.Where(r => r.IsApproved).Average(r => (double)r.Rating)
                    : 0.0)
                .ThenBy(p => p.Name),
            ProductSortOrder.Discount => query
                .OrderByDescending(p => p.CompareAtPrice.HasValue && p.CompareAtPrice.Value > p.BasePrice
                    ? (p.CompareAtPrice.Value - p.BasePrice) / p.CompareAtPrice.Value
                    : 0m)
                .ThenBy(p => p.Name),
            ProductSortOrder.Featured => query.OrderByDescending(p => p.IsFeatured).ThenBy(p => p.DisplayOrder).ThenByDescending(p => p.CreatedAtUtc),
            _ => query.OrderBy(p => p.DisplayOrder).ThenByDescending(p => p.CreatedAtUtc)
        };
    }

    private async Task<List<ProductListItemDto>> BuildItemsAsync(
        List<Guid> ids,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var details = await _context.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
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

        var detailsById = details.ToDictionary(d => d.Id);
        var colourMap = await CatalogQueryHelpers.GetColourMapAsync(_context, ids, cancellationToken);
        var stockMap = await CatalogQueryHelpers.GetStockMapAsync(_context, ids, cancellationToken);
        var ratingMap = await GetRatingMapAsync(ids, cancellationToken);

        return ids.Select(id =>
        {
            if (!detailsById.TryGetValue(id, out var d))
            {
                return null;
            }

            var colours = colourMap.TryGetValue(id, out var productColours)
                ? productColours
                : new List<HomeColourDto>();
            var stock = stockMap.TryGetValue(id, out var available) ? available : (int?)null;
            var (averageRating, reviewCount) = ratingMap.TryGetValue(id, out var rating) ? rating : (0.0, 0);

            return new ProductListItemDto(
                d.Id,
                d.Name,
                d.Slug,
                d.BrandName,
                CatalogQueryHelpers.ResolveImageUrl(_storage, d.ImageFileName),
                CatalogQueryHelpers.ResolveCardImageUrl(_storage, d.ImageFileName),
                d.ImageAltText,
                d.BasePrice,
                d.CompareAtPrice,
                CatalogQueryHelpers.CalculateDiscountPercent(d.BasePrice, d.CompareAtPrice),
                CatalogQueryHelpers.IsRecentlyCreated(d.CreatedAtUtc, d.IsNewArrival, now),
                stock.HasValue && stock.Value > 0,
                stock.HasValue && stock.Value > 0 && CatalogQueryHelpers.IsLowStock(stock.Value),
                colours,
                averageRating,
                reviewCount);
        }).Where(dto => dto != null).Select(dto => dto!).ToList();
    }

    private async Task<Dictionary<Guid, (double AverageRating, int ReviewCount)>> GetRatingMapAsync(
        List<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, (double, int)>();
        }

        var rows = await _context.ProductReviews
            .AsNoTracking()
            .Where(r => r.IsApproved && productIds.Contains(r.ProductId))
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Average = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ProductId, r => (r.Average, r.Count));
    }

    private async Task<(decimal? Min, decimal? Max)> GetPriceBoundsAsync(
        IQueryable<Product> filtered,
        CancellationToken cancellationToken)
    {
        var row = await filtered
            .GroupBy(p => 1)
            .Select(g => new { Min = g.Min(p => (decimal?)p.BasePrice), Max = g.Max(p => (decimal?)p.BasePrice) })
            .FirstOrDefaultAsync(cancellationToken);

        return (row?.Min, row?.Max);
    }

    private async Task<IReadOnlyList<FacetGroupDto>> BuildFacetsAsync(
        IQueryable<Product> filtered,
        ProductListQuery query,
        CancellationToken cancellationToken)
    {
        var facets = new List<FacetGroupDto>
        {
            await BuildCategoryFacetAsync(filtered, query.Category, cancellationToken),
            await BuildBrandFacetAsync(filtered, query.Brand, cancellationToken),
            await BuildCollectionFacetAsync(filtered, query.Collection, cancellationToken)
        };

        facets.AddRange(await BuildAttributeFacetsAsync(filtered, query, cancellationToken));

        facets.Add(await BuildMaterialFacetAsync(filtered, query.Material, cancellationToken));
        facets.Add(await BuildGenderFacetAsync(filtered, query.Gender, cancellationToken));
        facets.Add(await BuildTagFacetAsync(filtered, query.Tag, cancellationToken));
        facets.Add(await BuildRatingFacetAsync(filtered, query.MinRating, cancellationToken));

        return facets.Where(f => f.Values.Count > 0).ToList();
    }

    private async Task<FacetGroupDto> BuildCategoryFacetAsync(IQueryable<Product> filtered, string? selected, CancellationToken cancellationToken)
    {
        var counts = await filtered
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        if (counts.Count == 0)
        {
            return new FacetGroupDto("category", "Category", false, Array.Empty<FacetValueDto>());
        }

        var ids = counts.Select(c => c.CategoryId).ToList();
        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var byId = categories.ToDictionary(c => c.Id);
        var values = new List<FacetValueDto>();

        foreach (var count in counts.OrderBy(c => byId[c.CategoryId].ParentCategoryId != null).ThenBy(c => byId[c.CategoryId].Name))
        {
            if (!byId.TryGetValue(count.CategoryId, out var category))
            {
                continue;
            }

            var label = category.ParentCategoryId != null ? "\u00A0\u00A0" + category.Name : category.Name;
            values.Add(new FacetValueDto(category.Slug, label, count.Count, selected == category.Slug));
        }

        return new FacetGroupDto("category", "Category", false, values);
    }

    private async Task<FacetGroupDto> BuildBrandFacetAsync(IQueryable<Product> filtered, string? selected, CancellationToken cancellationToken)
    {
        var counts = await filtered
            .Where(p => p.Brand != null)
            .GroupBy(p => p.Brand!.Id)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        if (counts.Count == 0)
        {
            return new FacetGroupDto("brand", "Brand", false, Array.Empty<FacetValueDto>());
        }

        var ids = counts.Select(c => c.BrandId).ToList();
        var brands = await _context.Brands.AsNoTracking().Where(b => b.IsActive && ids.Contains(b.Id)).ToListAsync(cancellationToken);
        var byId = brands.ToDictionary(b => b.Id);

        var values = counts
            .Where(c => byId.ContainsKey(c.BrandId))
            .OrderBy(c => byId[c.BrandId].Name)
            .Select(c => new FacetValueDto(byId[c.BrandId].Slug, byId[c.BrandId].Name, c.Count, selected == byId[c.BrandId].Slug))
            .ToList();

        return new FacetGroupDto("brand", "Brand", false, values);
    }

    private async Task<FacetGroupDto> BuildCollectionFacetAsync(IQueryable<Product> filtered, string? selected, CancellationToken cancellationToken)
    {
        var counts = await filtered
            .Where(p => p.Collection != null)
            .GroupBy(p => p.Collection!.Id)
            .Select(g => new { CollectionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        if (counts.Count == 0)
        {
            return new FacetGroupDto("collection", "Collection", false, Array.Empty<FacetValueDto>());
        }

        var ids = counts.Select(c => c.CollectionId).ToList();
        var collections = await _context.Collections.AsNoTracking().Where(c => c.IsActive && ids.Contains(c.Id)).ToListAsync(cancellationToken);
        var byId = collections.ToDictionary(c => c.Id);

        var values = counts
            .Where(c => byId.ContainsKey(c.CollectionId))
            .OrderBy(c => byId[c.CollectionId].Name)
            .Select(c => new FacetValueDto(byId[c.CollectionId].Slug, byId[c.CollectionId].Name, c.Count, selected == byId[c.CollectionId].Slug))
            .ToList();

        return new FacetGroupDto("collection", "Collection", false, values);
    }

    private async Task<IReadOnlyList<FacetGroupDto>> BuildAttributeFacetsAsync(
        IQueryable<Product> filtered,
        ProductListQuery query,
        CancellationToken cancellationToken)
    {
        var result = new List<FacetGroupDto>();

        var colourCounts = await CountByAttributeValueAsync(filtered, ColourAttributeName, cancellationToken);
        if (colourCounts.Count > 0)
        {
            result.Add(new FacetGroupDto(
                "colour",
                "Colour",
                true,
                colourCounts.Select(c => new FacetValueDto(c.Slug, c.Name, c.Count, query.Colour.Contains(c.Slug))).ToList()));
        }

        var sizeCounts = await CountByAttributeValueAsync(filtered, SizeAttributeName, cancellationToken);
        if (sizeCounts.Count > 0)
        {
            result.Add(new FacetGroupDto(
                "size",
                "Size",
                true,
                sizeCounts.Select(c => new FacetValueDto(c.Slug, c.Name, c.Count, query.Size.Contains(c.Slug))).ToList()));
        }

        return result;
    }

    private async Task<List<(string Slug, string Name, int Count)>> CountByAttributeValueAsync(
        IQueryable<Product> filtered,
        string attributeName,
        CancellationToken cancellationToken)
    {
        var name = attributeName.ToLowerInvariant();
        var rows = await filtered
            .SelectMany(p => p.Variants)
            .Where(v => v.IsActive)
            .SelectMany(v => v.VariantAttributeValues)
            .Where(vav =>
                vav.AttributeValue != null &&
                vav.AttributeValue.ProductAttribute != null &&
                string.Equals(vav.AttributeValue.ProductAttribute.Name, name, StringComparison.OrdinalIgnoreCase))
            .GroupBy(vav => vav.AttributeValue!.Id)
            .Select(g => new
            {
                Id = g.Key,
                Count = g.Select(vav => vav.Variant!.ProductId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new List<(string, string, int)>();
        }

        var ids = rows.Select(r => r.Id).ToList();
        var values = await _context.ProductAttributeValues
            .AsNoTracking()
            .Where(v => v.IsActive && ids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        var byId = values.ToDictionary(v => v.Id);
        return rows
            .Where(r => byId.ContainsKey(r.Id))
            .OrderBy(r => byId[r.Id].DisplayOrder)
            .Select(r => (byId[r.Id].Slug, byId[r.Id].Name, r.Count))
            .ToList();
    }

    private async Task<FacetGroupDto> BuildMaterialFacetAsync(IQueryable<Product> filtered, string[] selected, CancellationToken cancellationToken)
    {
        var rows = await filtered
            .Where(p => p.Material != null)
            .GroupBy(p => p.Material)
            .Select(g => new { Material = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var values = rows
            .OrderBy(r => r.Material)
            .Select(r => new FacetValueDto(r.Material!, r.Material!, r.Count, selected.Contains(r.Material!)))
            .ToList();

        return new FacetGroupDto("material", "Material", true, values);
    }

    private async Task<FacetGroupDto> BuildGenderFacetAsync(IQueryable<Product> filtered, string? selected, CancellationToken cancellationToken)
    {
        var rows = await filtered
            .Where(p => p.Gender != null)
            .GroupBy(p => p.Gender)
            .Select(g => new { Gender = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var values = rows
            .OrderBy(r => r.Gender)
            .Select(r => new FacetValueDto(r.Gender!.ToLowerInvariant(), r.Gender!, r.Count, string.Equals(selected, r.Gender, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new FacetGroupDto("gender", "Gender", false, values);
    }

    private async Task<FacetGroupDto> BuildTagFacetAsync(IQueryable<Product> filtered, string[] selected, CancellationToken cancellationToken)
    {
        var rows = await filtered
            .SelectMany(p => p.ProductTagMappings)
            .Where(m => m.ProductTag != null)
            .GroupBy(m => m.ProductTag!.Id)
            .Select(g => new { TagId = g.Key, Count = g.Select(m => m.ProductId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new FacetGroupDto("tag", "Tag", true, Array.Empty<FacetValueDto>());
        }

        var ids = rows.Select(r => r.TagId).ToList();
        var tags = await _context.ProductTags.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);
        var byId = tags.ToDictionary(t => t.Id);

        var values = rows
            .Where(r => byId.ContainsKey(r.TagId))
            .OrderBy(r => byId[r.TagId].Name)
            .Select(r => new FacetValueDto(byId[r.TagId].Slug, byId[r.TagId].Name, r.Count, selected.Contains(byId[r.TagId].Slug)))
            .ToList();

        return new FacetGroupDto("tag", "Tag", true, values);
    }

    private async Task<FacetGroupDto> BuildRatingFacetAsync(IQueryable<Product> filtered, int? selected, CancellationToken cancellationToken)
    {
        var values = new List<FacetValueDto>();
        for (var min = 4; min >= 1; min--)
        {
            var count = await filtered
                .Where(p => p.Reviews.Any(r => r.IsApproved) &&
                    p.Reviews.Where(r => r.IsApproved).Average(r => (double)r.Rating) >= min)
                .CountAsync(cancellationToken);

            values.Add(new FacetValueDto(
                min.ToString(System.Globalization.CultureInfo.InvariantCulture),
                min == 4 ? "4 & up" : $"{min} & up",
                count,
                selected == min));
        }

        return new FacetGroupDto("rating", "Rating", false, values);
    }

    private static string NormalizeSearchTerm(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(term.Trim(), @"\s+", " ");
    }

    private static string[] CleanValues(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
