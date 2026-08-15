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

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CategoryService> _logger;
    private readonly CacheSettings _cacheSettings;

    public CategoryService(
        AppDbContext context,
        IDistributedCache cache,
        ILogger<CategoryService> logger,
        CacheSettings cacheSettings)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _cacheSettings = cacheSettings;
    }

    public async Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = $"category:{id}";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<CategoryDto>(cached);

        var category = await _context.Categories
            .Include(c => c.ParentCategory)
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category == null) return null;

        var dto = ToDto(category, await _context.Categories.CountAsync(c => c.ParentCategoryId == id, cancellationToken));

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(dto), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes)
        }, cancellationToken);

        return dto;
    }

    public async Task<IEnumerable<CategoryDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories
            .Include(c => c.ParentCategory)
            .Include(c => c.Children)
            .AsNoTracking();

        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        var categories = await query.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var childrenCounts = await _context.Categories
            .Where(c => c.ParentCategoryId.HasValue)
            .GroupBy(c => c.ParentCategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return categories.Select(c => ToDto(c, childrenCounts.TryGetValue(c.Id, out var count) ? count : 0));
    }

    public async Task<IEnumerable<CategoryHierarchyDto>> GetHierarchyAsync(CancellationToken cancellationToken = default)
    {
        var key = "categories:hierarchy";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<IEnumerable<CategoryHierarchyDto>>(cached)!;

        var rootCategories = await _context.Categories
            .Where(c => c.ParentCategoryId == null && c.IsActive)
            .Include(c => c.Children)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var hierarchy = rootCategories.Select(c => BuildHierarchyNode(c)).ToList();

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(hierarchy), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes)
        }, cancellationToken);

        return hierarchy;
    }

    public async Task<IEnumerable<CategoryDto>> GetMenuCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Categories
            .Where(c => c.IsActive && c.ShowInMainMenu && c.ParentCategoryId == null)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.DisplayOrder, c.Description, c.ParentCategoryId, null,
                0, c.ImageUrl, c.IconUrl, c.IsActive, c.ShowInMainMenu,
                c.SeoTitle, c.SeoDescription, c.CreatedAtUtc, c.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSlugUniqueAsync(GenerateSlug(request.Name), null, cancellationToken))
            throw new InvalidOperationException($"Category with slug '{GenerateSlug(request.Name)}' already exists");

        var category = new Category
        {
            Name = request.Name,
            Slug = GenerateSlug(request.Name),
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            DisplayOrder = request.DisplayOrder,
            ImageUrl = request.ImageUrl,
            IconUrl = request.IconUrl,
            IsActive = request.IsActive,
            ShowInMainMenu = request.ShowInMainMenu,
            SeoTitle = request.SeoTitle,
            SeoDescription = request.SeoDescription
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        await InvalidateCategoryCacheAsync(cancellationToken);

        _logger.LogInformation("Created category {CategoryId} - {Name}", category.Id, category.Name);
        return ToDto(category, 0);
    }

    public async Task<CategoryDto?> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories.FindAsync(new object[] { request.Id }, cancellationToken);
        if (category == null) return null;

        if (!await IsSlugUniqueAsync(GenerateSlug(request.Name), request.Id, cancellationToken))
            throw new InvalidOperationException($"Category with slug '{GenerateSlug(request.Name)}' already exists");

        if (await HasCircularReferenceAsync(request.Id, request.ParentCategoryId, cancellationToken))
            throw new InvalidOperationException("Cannot set this parent - it would create a circular reference");

        category.Name = request.Name;
        category.Slug = GenerateSlug(request.Name);
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;
        category.DisplayOrder = request.DisplayOrder;
        category.ImageUrl = request.ImageUrl;
        category.IconUrl = request.IconUrl;
        category.IsActive = request.IsActive;
        category.ShowInMainMenu = request.ShowInMainMenu;
        category.SeoTitle = request.SeoTitle;
        category.SeoDescription = request.SeoDescription;
        category.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCategoryCacheAsync(cancellationToken);

        _logger.LogInformation("Updated category {CategoryId}", request.Id);
        return ToDto(category, await _context.Categories.CountAsync(c => c.ParentCategoryId == request.Id, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories.FindAsync(new object[] { id }, cancellationToken);
        if (category == null) return false;

        if (await HasProductsAsync(id, cancellationToken))
            throw new InvalidOperationException("Cannot delete category that has products");

        if (await _context.Categories.AnyAsync(c => c.ParentCategoryId == id, cancellationToken))
            throw new InvalidOperationException("Cannot delete category with child categories");

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCategoryCacheAsync(cancellationToken);

        _logger.LogInformation("Deleted category {CategoryId}", id);
        return true;
    }

    public async Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories.Where(c => c.Slug == slug);
        if (excludeId.HasValue)
            query = query.Where(c => c.Id != excludeId);
        return !await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> HasCircularReferenceAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default)
    {
        if (parentId == null) return false;
        if (parentId == id) return true;

        var currentParentId = parentId;
        var visited = new HashSet<Guid>();

        while (currentParentId.HasValue)
        {
            if (currentParentId == id || !visited.Add(currentParentId.Value))
                return true;

            var parent = await _context.Categories.FindAsync(new object[] { currentParentId }, cancellationToken);
            currentParentId = parent?.ParentCategoryId;
        }

        return false;
    }

    public async Task<bool> HasProductsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Product>().AnyAsync(p => p.CategoryId == id, cancellationToken);
    }

    public async Task<IEnumerable<CategoryDto>> SearchAsync(string searchTerm, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories
            .Include(c => c.ParentCategory)
            .AsNoTracking();

        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        var results = await query
            .Where(c => c.Name.Contains(searchTerm) || (c.Description != null && c.Description.Contains(searchTerm)))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return results.Select(c => ToDto(c, 0));
    }

    public async Task ReorderAsync(IEnumerable<(Guid Id, int Order)> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var category = await _context.Categories.FindAsync(new object[] { item.Id }, cancellationToken);
            if (category != null)
            {
                category.DisplayOrder = item.Order;
                category.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateCategoryCacheAsync(cancellationToken);
    }

    private CategoryDto ToDto(Category category, int childrenCount) => new(
        category.Id,
        category.Name,
        category.Slug,
        category.DisplayOrder,
        category.Description,
        category.ParentCategoryId,
        category.ParentCategory?.Name,
        childrenCount,
        category.ImageUrl,
        category.IconUrl,
        category.IsActive,
        category.ShowInMainMenu,
        category.SeoTitle,
        category.SeoDescription,
        category.CreatedAtUtc,
        category.UpdatedAtUtc
    );

    private CategoryHierarchyDto BuildHierarchyNode(Category category) => new(
        category.Id,
        category.Name,
        category.Slug,
        category.IconUrl,
        category.DisplayOrder,
        category.Children.Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).Select(BuildHierarchyNode)
    );

    private string GenerateSlug(string name) => name.ToLowerInvariant()
        .Replace(" ", "-")
        .Replace("--", "-")
        .Trim('-');

    private async Task InvalidateCategoryCacheAsync(CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync("categories:hierarchy", cancellationToken);
        await _cache.RemoveAsync(Application.Common.CacheKeys.HomePage, cancellationToken);
        await _cache.RemoveAsync(Application.Common.CacheKeys.Sitemap, cancellationToken);
    }
}
