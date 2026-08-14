using System.Text.Json;
using System.Text.RegularExpressions;
using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Content;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Content management implementation. All write paths sanitize rich HTML via
/// <see cref="RichContentSanitizer"/>, validate image/link URLs, and invalidate
/// the relevant cache keys so the storefront picks up changes immediately.
/// Storefront read paths return only published, in-schedule records.
/// </summary>
public sealed class ContentManagementService : IContentManagementService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly CacheSettings _cacheSettings;
    private readonly ILogger<ContentManagementService> _logger;

    public ContentManagementService(
        AppDbContext context,
        IDistributedCache cache,
        CacheSettings cacheSettings,
        ILogger<ContentManagementService> logger)
    {
        _context = context;
        _cache = cache;
        _cacheSettings = cacheSettings;
        _logger = logger;
    }

    // ---- Pages ----

    public async Task<ContentPageListResult> GetPagesAsync(ContentPageQuery query, CancellationToken cancellationToken = default)
    {
        var q = _context.ContentPages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(p =>
                p.Title.Contains(search) ||
                p.Slug.Contains(search));
        }

        var total = await q.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var items = await q
            .OrderByDescending(p => p.IsSystem)
            .ThenByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ContentPageListItemDto(
                p.Id, p.Title, p.Slug, p.Status, p.IsSystem, p.PublishedAtUtc, p.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return new ContentPageListResult(items, total, page, pageSize);
    }

    public async Task<ContentPageDto?> GetPageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _context.ContentPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return page is null ? null : ToDto(page);
    }

    public async Task<ContentPageDto?> GetPageBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var page = await _context.ContentPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);
        return page is null ? null : ToDto(page);
    }

    public async Task<ContentMutationResult> CreatePageAsync(ContentPageRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidatePageRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, Guid.Empty, validation);
        }

        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await _context.ContentPages.AnyAsync(p => p.Slug == slug, cancellationToken))
        {
            return new ContentMutationResult(false, Guid.Empty, "A page with this slug already exists.");
        }

        var now = DateTime.UtcNow;
        var page = new ContentPage
        {
            Title = request.Title.Trim(),
            Slug = slug,
            Summary = SanitizeSummary(request.Summary),
            BodyHtml = RichContentSanitizer.Sanitize(request.BodyHtml),
            Template = request.Template,
            Status = request.Status,
            PublishedAtUtc = request.Status == ContentStatus.Published ? now : null,
            MetaTitle = request.MetaTitle?.Trim(),
            MetaDescription = request.MetaDescription?.Trim(),
            CreatedAtUtc = now,
            CreatedBy = actorId
        };

        _context.ContentPages.Add(page);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, page.Id);
    }

    public async Task<ContentMutationResult> UpdatePageAsync(Guid id, ContentPageRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidatePageRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, id, validation);
        }

        var page = await _context.ContentPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
        {
            return new ContentMutationResult(false, id, "Page not found.");
        }

        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await _context.ContentPages.AnyAsync(p => p.Slug == slug && p.Id != id, cancellationToken))
        {
            return new ContentMutationResult(false, id, "A page with this slug already exists.");
        }

        var wasPublished = page.Status == ContentStatus.Published;
        page.Title = request.Title.Trim();
        page.Slug = slug;
        page.Summary = SanitizeSummary(request.Summary);
        page.BodyHtml = RichContentSanitizer.Sanitize(request.BodyHtml);
        page.Template = request.Template;
        page.Status = request.Status;
        page.MetaTitle = request.MetaTitle?.Trim();
        page.MetaDescription = request.MetaDescription?.Trim();

        if (!wasPublished && request.Status == ContentStatus.Published && page.PublishedAtUtc is null)
        {
            page.PublishedAtUtc = DateTime.UtcNow;
        }

        page.UpdatedAtUtc = DateTime.UtcNow;
        page.UpdatedBy = actorId;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    public async Task<ContentMutationResult> DeletePageAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var page = await _context.ContentPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
        {
            return new ContentMutationResult(false, id, "Page not found.");
        }

        if (page.IsSystem)
        {
            return new ContentMutationResult(false, id, "System pages cannot be deleted. They can be hidden by setting the status to Draft.");
        }

        _context.ContentPages.Remove(page);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    // ---- Banners ----

    public async Task<IReadOnlyList<BannerDto>> GetBannersAsync(CancellationToken cancellationToken = default)
    {
        var banners = await _context.Banners.AsNoTracking()
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);

        return banners.Select(b => ToDto(b)).ToList();
    }

    public async Task<IReadOnlyList<BannerDto>> GetActiveBannersAsync(BannerPlacement placement, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.Banners, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            var all = JsonSerializer.Deserialize<IReadOnlyList<BannerDto>>(cached)!;
            return all.Where(b => b.Placement == placement).ToList();
        }

        var now = DateTime.UtcNow;
        var banners = await _context.Banners.AsNoTracking()
            .Where(b =>
                b.Status == ContentStatus.Published &&
                (b.StartAtUtc == null || b.StartAtUtc <= now) &&
                (b.EndAtUtc == null || b.EndAtUtc > now))
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var dtos = banners.Select(b => ToDto(b)).ToList();
        await _cache.SetStringAsync(CacheKeys.Banners, JsonSerializer.Serialize(dtos), GetCacheOptions(), cancellationToken);
        return dtos.Where(b => b.Placement == placement).ToList();
    }

    public async Task<ContentMutationResult> CreateBannerAsync(BannerRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidateBannerRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, Guid.Empty, validation);
        }

        var now = DateTime.UtcNow;
        var banner = new Banner
        {
            Name = request.Name.Trim(),
            Title = request.Title?.Trim(),
            Subtitle = request.Subtitle?.Trim(),
            ImageUrl = CleanUrl(request.ImageUrl),
            LinkUrl = CleanUrl(request.LinkUrl),
            LinkText = request.LinkText?.Trim(),
            Style = string.IsNullOrWhiteSpace(request.Style) ? "primary" : request.Style.Trim(),
            Placement = request.Placement,
            DisplayOrder = request.DisplayOrder,
            Status = request.Status,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            CreatedAtUtc = now,
            CreatedBy = actorId
        };

        _context.Banners.Add(banner);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, banner.Id);
    }

    public async Task<ContentMutationResult> UpdateBannerAsync(Guid id, BannerRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidateBannerRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, id, validation);
        }

        var banner = await _context.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (banner is null)
        {
            return new ContentMutationResult(false, id, "Banner not found.");
        }

        banner.Name = request.Name.Trim();
        banner.Title = request.Title?.Trim();
        banner.Subtitle = request.Subtitle?.Trim();
        banner.ImageUrl = CleanUrl(request.ImageUrl);
        banner.LinkUrl = CleanUrl(request.LinkUrl);
        banner.LinkText = request.LinkText?.Trim();
        banner.Style = string.IsNullOrWhiteSpace(request.Style) ? "primary" : request.Style.Trim();
        banner.Placement = request.Placement;
        banner.DisplayOrder = request.DisplayOrder;
        banner.Status = request.Status;
        banner.StartAtUtc = request.StartAtUtc;
        banner.EndAtUtc = request.EndAtUtc;
        banner.UpdatedAtUtc = DateTime.UtcNow;
        banner.UpdatedBy = actorId;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    public async Task<ContentMutationResult> DeleteBannerAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var banner = await _context.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (banner is null)
        {
            return new ContentMutationResult(false, id, "Banner not found.");
        }

        _context.Banners.Remove(banner);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    // ---- Homepage sections ----

    public async Task<IReadOnlyList<HomepageSectionDto>> GetHomepageSectionsAsync(CancellationToken cancellationToken = default)
    {
        var sections = await _context.HomepageSections.AsNoTracking()
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Title)
            .ToListAsync(cancellationToken);

        return sections.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<HomepageSectionDto>> GetActiveHomepageSectionsAsync(CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.HomepageSections, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<IReadOnlyList<HomepageSectionDto>>(cached)!;
        }

        var now = DateTime.UtcNow;
        var sections = await _context.HomepageSections.AsNoTracking()
            .Where(s =>
                s.Status == ContentStatus.Published &&
                (s.StartAtUtc == null || s.StartAtUtc <= now) &&
                (s.EndAtUtc == null || s.EndAtUtc > now))
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Title)
            .ToListAsync(cancellationToken);

        var dtos = sections.Select(ToDto).ToList();
        await _cache.SetStringAsync(CacheKeys.HomepageSections, JsonSerializer.Serialize(dtos), GetCacheOptions(), cancellationToken);
        return dtos;
    }

    public async Task<ContentMutationResult> CreateHomepageSectionAsync(HomepageSectionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidateHomepageSectionRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, Guid.Empty, validation);
        }

        var now = DateTime.UtcNow;
        var section = new HomepageSection
        {
            SectionType = request.SectionType.Trim(),
            Title = request.Title.Trim(),
            Subtitle = request.Subtitle?.Trim(),
            ContentJson = request.ContentJson,
            Html = RichContentSanitizer.Sanitize(request.Html),
            DisplayOrder = request.DisplayOrder,
            Status = request.Status,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            CreatedAtUtc = now,
            CreatedBy = actorId
        };

        _context.HomepageSections.Add(section);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, section.Id);
    }

    public async Task<ContentMutationResult> UpdateHomepageSectionAsync(Guid id, HomepageSectionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidateHomepageSectionRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, id, validation);
        }

        var section = await _context.HomepageSections.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (section is null)
        {
            return new ContentMutationResult(false, id, "Homepage section not found.");
        }

        section.SectionType = request.SectionType.Trim();
        section.Title = request.Title.Trim();
        section.Subtitle = request.Subtitle?.Trim();
        section.ContentJson = request.ContentJson;
        section.Html = RichContentSanitizer.Sanitize(request.Html);
        section.DisplayOrder = request.DisplayOrder;
        section.Status = request.Status;
        section.StartAtUtc = request.StartAtUtc;
        section.EndAtUtc = request.EndAtUtc;
        section.UpdatedAtUtc = DateTime.UtcNow;
        section.UpdatedBy = actorId;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    public async Task<ContentMutationResult> DeleteHomepageSectionAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var section = await _context.HomepageSections.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (section is null)
        {
            return new ContentMutationResult(false, id, "Homepage section not found.");
        }

        _context.HomepageSections.Remove(section);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    // ---- Navigation ----

    public async Task<IReadOnlyList<NavigationMenuDto>> GetNavigationMenusAsync(CancellationToken cancellationToken = default)
    {
        var menus = await _context.NavigationMenus.AsNoTracking()
            .Include(m => m.Items)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);

        return menus.Select(ToDto).ToList();
    }

    public async Task<NavigationMenuDto?> GetNavigationMenuByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeys.NavigationMenu.Replace("{code}", code);
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<NavigationMenuDto>(cached);
        }

        var menu = await _context.NavigationMenus.AsNoTracking()
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Code == code && m.IsActive, cancellationToken);

        if (menu is null)
        {
            return null;
        }

        var dto = ToDto(menu);
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), GetCacheOptions(), cancellationToken);
        return dto;
    }

    public async Task<ContentMutationResult> SaveNavigationMenuAsync(Guid id, NavigationMenuRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return new ContentMutationResult(false, id, "Name and code are required.");
        }

        var menu = await _context.NavigationMenus
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (menu is null)
        {
            return new ContentMutationResult(false, id, "Navigation menu not found.");
        }

        var code = request.Code.Trim().ToLowerInvariant();
        if (await _context.NavigationMenus.AnyAsync(m => m.Code == code && m.Id != id, cancellationToken))
        {
            return new ContentMutationResult(false, id, "A menu with this code already exists.");
        }

        menu.Name = request.Name.Trim();
        menu.Code = code;
        menu.Description = request.Description?.Trim();
        menu.IsActive = request.IsActive;

        var incoming = request.Items?.ToList() ?? new List<NavigationItemRequest>();
        var incomingIds = incoming.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        foreach (var existing in menu.Items.Where(i => !incomingIds.Contains(i.Id)).ToList())
        {
            _context.NavigationItems.Remove(existing);
        }

        foreach (var item in incoming)
        {
            var existingItem = item.Id.HasValue
                ? menu.Items.FirstOrDefault(i => i.Id == item.Id!.Value)
                : null;

            if (existingItem is not null)
            {
                existingItem.ParentId = item.ParentId;
                existingItem.Label = item.Label.Trim();
                existingItem.Url = CleanUrl(item.Url) ?? "#";
                existingItem.Target = string.IsNullOrWhiteSpace(item.Target) ? null : item.Target.Trim();
                existingItem.DisplayOrder = item.DisplayOrder;
                existingItem.IsActive = item.IsActive;
                existingItem.UpdatedAtUtc = DateTime.UtcNow;
                existingItem.UpdatedBy = actorId;
            }
            else
            {
                var newItem = new NavigationItem
                {
                    MenuId = menu.Id,
                    ParentId = item.ParentId,
                    Label = item.Label.Trim(),
                    Url = CleanUrl(item.Url) ?? "#",
                    Target = string.IsNullOrWhiteSpace(item.Target) ? null : item.Target.Trim(),
                    DisplayOrder = item.DisplayOrder,
                    IsActive = item.IsActive,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = actorId
                };
                _context.NavigationItems.Add(newItem);
            }
        }

        menu.UpdatedAtUtc = DateTime.UtcNow;
        menu.UpdatedBy = actorId;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    // ---- FAQs ----

    public async Task<IReadOnlyList<FaqItemDto>> GetFaqItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _context.FaqItems.AsNoTracking()
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.Question)
            .ToListAsync(cancellationToken);

        return items.Select(f => new FaqItemDto(
            f.Id, f.Question, f.Answer, f.Category, f.DisplayOrder, f.IsActive, f.UpdatedAtUtc)).ToList();
    }

    public async Task<ContentMutationResult> CreateFaqItemAsync(FaqItemRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidateFaqRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, Guid.Empty, validation);
        }

        var now = DateTime.UtcNow;
        var item = new FaqItem
        {
            Question = request.Question.Trim(),
            Answer = RichContentSanitizer.Sanitize(request.Answer),
            Category = request.Category?.Trim(),
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            CreatedBy = actorId
        };

        _context.FaqItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, item.Id);
    }

    public async Task<ContentMutationResult> UpdateFaqItemAsync(Guid id, FaqItemRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation = ValidateFaqRequest(request);
        if (validation is not null)
        {
            return new ContentMutationResult(false, id, validation);
        }

        var item = await _context.FaqItems.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (item is null)
        {
            return new ContentMutationResult(false, id, "FAQ item not found.");
        }

        item.Question = request.Question.Trim();
        item.Answer = RichContentSanitizer.Sanitize(request.Answer);
        item.Category = request.Category?.Trim();
        item.DisplayOrder = request.DisplayOrder;
        item.IsActive = request.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedBy = actorId;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    public async Task<ContentMutationResult> DeleteFaqItemAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var item = await _context.FaqItems.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (item is null)
        {
            return new ContentMutationResult(false, id, "FAQ item not found.");
        }

        _context.FaqItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    // ---- Blog posts (preparation) ----

    public async Task<IReadOnlyList<BlogPostDto>> GetBlogPostsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.BlogPosts.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new BlogPostDto(
                b.Id, b.Title, b.Slug, b.Excerpt, b.ContentHtml, b.CoverImageUrl,
                b.Status, b.PublishedAtUtc, b.AuthorName))
            .ToListAsync(cancellationToken);
    }

    // ---- Policy documents ----

    public async Task<IReadOnlyList<PolicyDocumentDto>> GetPolicyDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PolicyDocuments.AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new PolicyDocumentDto(
                p.Id, p.Code, p.Title, p.Summary, p.BodyHtml, p.Status, p.PublishedAtUtc, p.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<PolicyDocumentDto?> GetPolicyDocumentByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeys.PolicyDocument.Replace("{code}", code);
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<PolicyDocumentDto>(cached);
        }

        var document = await _context.PolicyDocuments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var dto = new PolicyDocumentDto(
            document.Id, document.Code, document.Title, document.Summary, document.BodyHtml,
            document.Status, document.PublishedAtUtc, document.UpdatedAtUtc);

        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), GetCacheOptions(), cancellationToken);
        return dto;
    }

    public async Task<ContentMutationResult> UpdatePolicyDocumentAsync(Guid id, PolicyDocumentRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return new ContentMutationResult(false, id, "Title is required.");
        }

        var document = await _context.PolicyDocuments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (document is null)
        {
            return new ContentMutationResult(false, id, "Policy document not found.");
        }

        var wasPublished = document.Status == ContentStatus.Published;
        document.Title = request.Title.Trim();
        document.Summary = SanitizeSummary(request.Summary);
        document.BodyHtml = RichContentSanitizer.Sanitize(request.BodyHtml);
        document.Status = request.Status;

        if (!wasPublished && request.Status == ContentStatus.Published && document.PublishedAtUtc is null)
        {
            document.PublishedAtUtc = DateTime.UtcNow;
        }

        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedBy = actorId;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateContentCacheAsync(cancellationToken);

        return new ContentMutationResult(true, id);
    }

    // ---- Cache ----

    public async Task InvalidateContentCacheAsync(CancellationToken cancellationToken = default)
    {
        var keys = new[]
        {
            CacheKeys.ContentPages,
            CacheKeys.Banners,
            CacheKeys.HomepageSections,
            CacheKeys.FaqItems,
            CacheKeys.PolicyDocuments,
            CacheKeys.HomePage,
            CacheKeys.Sitemap
        };

        foreach (var key in keys)
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }

        foreach (var menu in await _context.NavigationMenus.AsNoTracking().Select(m => m.Code).ToListAsync(cancellationToken))
        {
            await _cache.RemoveAsync(CacheKeys.NavigationMenu.Replace("{code}", menu), cancellationToken);
        }

        foreach (var doc in await _context.PolicyDocuments.AsNoTracking().Select(p => p.Code).ToListAsync(cancellationToken))
        {
            await _cache.RemoveAsync(CacheKeys.PolicyDocument.Replace("{code}", doc), cancellationToken);
        }
    }

    // ---- Helpers ----

    private static string? SanitizeSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = RichContentSanitizer.ToPlainText(value);
        return text.Length > 500 ? text[..500] : text;
    }

    private static string? CleanUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return RichContentSanitizer.IsSafeUrl(trimmed) ? trimmed : null;
    }

    private static string? ValidatePageRequest(ContentPageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Title is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return "Slug is required.";
        }

        if (!SlugRegex.IsMatch(request.Slug))
        {
            return "Slug may only contain lowercase letters, numbers, dashes and underscores.";
        }

        return null;
    }

    private static string? ValidateBannerRequest(BannerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (!string.IsNullOrWhiteSpace(request.ImageUrl) && !RichContentSanitizer.IsSafeUrl(request.ImageUrl))
        {
            return "Image URL is not valid.";
        }

        if (!string.IsNullOrWhiteSpace(request.LinkUrl) && !RichContentSanitizer.IsSafeUrl(request.LinkUrl))
        {
            return "Link URL is not valid.";
        }

        if (request.StartAtUtc.HasValue && request.EndAtUtc.HasValue && request.StartAtUtc.Value >= request.EndAtUtc.Value)
        {
            return "Start date must be before the end date.";
        }

        return null;
    }

    private static string? ValidateHomepageSectionRequest(HomepageSectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SectionType))
        {
            return "Section type is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Title is required.";
        }

        if (request.StartAtUtc.HasValue && request.EndAtUtc.HasValue && request.StartAtUtc.Value >= request.EndAtUtc.Value)
        {
            return "Start date must be before the end date.";
        }

        return null;
    }

    private static string? ValidateFaqRequest(FaqItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return "Question is required.";
        }

        if (!string.IsNullOrWhiteSpace(request.Answer) && request.Answer.Length > 4000)
        {
            return "Answer is too long.";
        }

        return null;
    }

    private static ContentPageDto ToDto(ContentPage page) => new(
        page.Id, page.Title, page.Slug, page.Summary, page.BodyHtml, page.Template,
        page.Status, page.IsSystem, page.PublishedAtUtc, page.MetaTitle, page.MetaDescription,
        page.CreatedAtUtc, page.UpdatedAtUtc);

    private static BannerDto ToDto(Banner banner) => new(
        banner.Id, banner.Name, banner.Title, banner.Subtitle, banner.ImageUrl, banner.LinkUrl,
        banner.LinkText, banner.Style, banner.Placement, banner.DisplayOrder, banner.Status,
        banner.StartAtUtc, banner.EndAtUtc);

    private static HomepageSectionDto ToDto(HomepageSection section) => new(
        section.Id, section.SectionType, section.Title, section.Subtitle, section.ContentJson,
        section.Html, section.DisplayOrder, section.Status, section.StartAtUtc, section.EndAtUtc);

    private static NavigationMenuDto ToDto(NavigationMenu menu) => new(
        menu.Id, menu.Name, menu.Code, menu.Description, menu.IsActive,
        menu.Items
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new NavigationItemDto(
                i.Id, i.ParentId, i.Label, i.Url, i.Target, i.DisplayOrder, i.IsActive))
            .ToList());

    private static readonly Regex SlugRegex = new(
        @"^[a-z0-9][a-z0-9_-]*$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private DistributedCacheEntryOptions GetCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes),
        SlidingExpiration = TimeSpan.FromMinutes(_cacheSettings.SlidingExpirationMinutes)
    };
}
