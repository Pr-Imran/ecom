using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Content;
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

public class ContentManagementServiceTests
{
    private readonly IDistributedCache _cache = new MemoryDistributedCache(
        new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-content-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private ContentManagementService CreateService(AppDbContext context)
        => new(context, _cache, new CacheSettings { AbsoluteExpirationMinutes = 10 }, NullLogger<ContentManagementService>.Instance);

    // ---- Sanitization ----

    [Fact]
    public async Task CreatePage_SanitizesRichBody()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreatePageAsync(
            new ContentPageRequest("Test", "test", null, "<p>Hi</p><script>alert(1)</script>", ContentPageTemplate.Default, ContentStatus.Published, null, null),
            "tester",
            CancellationToken.None);

        Assert.True(result.Success);
        var page = await context.ContentPages.FirstAsync();
        Assert.DoesNotContain("script", page.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hi", page.BodyHtml);
    }

    // ---- Slug validation ----

    [Fact]
    public async Task CreatePage_RejectsInvalidSlug()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreatePageAsync(
            new ContentPageRequest("Test", "Has Spaces!", null, null, ContentPageTemplate.Default, ContentStatus.Draft, null, null),
            "tester",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("slug", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePage_RejectsDuplicateSlug()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var first = await service.CreatePageAsync(
            new ContentPageRequest("One", "same-slug", null, null, ContentPageTemplate.Default, ContentStatus.Draft, null, null),
            "tester",
            CancellationToken.None);
        Assert.True(first.Success);

        var second = await service.CreatePageAsync(
            new ContentPageRequest("Two", "same-slug", null, null, ContentPageTemplate.Default, ContentStatus.Draft, null, null),
            "tester",
            CancellationToken.None);

        Assert.False(second.Success);
        Assert.Contains("already exists", second.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---- URL validation ----

    [Fact]
    public async Task CreateBanner_RejectsUnsafeImageUrl()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateBannerAsync(
            new BannerRequest("Bad", null, null, "javascript:alert(1)", null, null, "primary", BannerPlacement.Homepage, 0, ContentStatus.Published, null, null),
            "tester",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Image URL", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateBanner_ValidatesScheduleWindow()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateBannerAsync(
            new BannerRequest("Bad Window", null, null, null, null, null, "primary", BannerPlacement.Homepage, 0, ContentStatus.Published, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)),
            "tester",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Start date must be before the end date", result.ErrorMessage);
    }

    // ---- Scheduling ----

    [Fact]
    public async Task GetActiveBanners_RespectsScheduleWindow()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var now = DateTime.UtcNow;
        var active = new Banner { Name = "Active", Placement = BannerPlacement.Homepage, DisplayOrder = 0, Status = ContentStatus.Published, StartAtUtc = null, EndAtUtc = null, CreatedAtUtc = now };
        var notYetStarted = new Banner { Name = "Future", Placement = BannerPlacement.Homepage, DisplayOrder = 1, Status = ContentStatus.Published, StartAtUtc = now.AddHours(2), EndAtUtc = null, CreatedAtUtc = now };
        var expired = new Banner { Name = "Expired", Placement = BannerPlacement.Homepage, DisplayOrder = 2, Status = ContentStatus.Published, StartAtUtc = null, EndAtUtc = now.AddHours(-2), CreatedAtUtc = now };
        var draft = new Banner { Name = "Draft", Placement = BannerPlacement.Announcement, DisplayOrder = 3, Status = ContentStatus.Draft, CreatedAtUtc = now };

        context.Banners.AddRange(active, notYetStarted, expired, draft);
        await context.SaveChangesAsync();

        var result = await service.GetActiveBannersAsync(BannerPlacement.Homepage, CancellationToken.None);

        var names = result.Select(b => b.Name).ToArray();
        Assert.Equal(new[] { "Active" }, names);
    }

    [Fact]
    public async Task GetActiveHomepageSections_RespectsScheduleWindow()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var now = DateTime.UtcNow;
        var section = new HomepageSection { SectionType = "rich", Title = "Active", DisplayOrder = 0, Status = ContentStatus.Published, StartAtUtc = null, EndAtUtc = null, CreatedAtUtc = now };
        var future = new HomepageSection { SectionType = "rich", Title = "Future", DisplayOrder = 1, Status = ContentStatus.Published, StartAtUtc = now.AddHours(2), EndAtUtc = null, CreatedAtUtc = now };
        context.HomepageSections.AddRange(section, future);
        await context.SaveChangesAsync();

        var result = await service.GetActiveHomepageSectionsAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Active", result[0].Title);
    }

    // ---- Navigation hierarchy ----

    [Fact]
    public async Task SaveNavigationMenu_PersistsHierarchyAndLabels()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var menu = new NavigationMenu
        {
            Name = "Main Menu",
            Code = "main",
            Description = "Primary",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "system"
        };
        context.NavigationMenus.Add(menu);
        await context.SaveChangesAsync();

        var parent = new NavigationItem { Label = "Shop", Url = "/products", DisplayOrder = 0, IsActive = true, MenuId = menu.Id, CreatedAtUtc = DateTime.UtcNow };
        context.NavigationItems.Add(parent);
        await context.SaveChangesAsync();

        var request = new NavigationMenuRequest(
            "Main Menu", "main", "Primary", true,
            new[]
            {
                new NavigationItemRequest(parent.Id, null, "Shop", "/products", null, 0, true),
                new NavigationItemRequest(null, parent.Id, "New Arrivals", "/products/new", null, 1, true),
                new NavigationItemRequest(null, parent.Id, "On Sale", "/products/sale", "_blank", 2, true)
            });

        var result = await service.SaveNavigationMenuAsync(menu.Id, request, "admin", CancellationToken.None);

        Assert.True(result.Success);

        var loaded = await service.GetNavigationMenuByCodeAsync("main", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Items.Count);

        var shop = loaded.Items.Single(i => i.Label == "Shop");
        var child = loaded.Items.Single(i => i.Label == "New Arrivals");
        var sale = loaded.Items.Single(i => i.Label == "On Sale");

        Assert.Null(shop.ParentId);
        Assert.Equal(shop.Id, child.ParentId);
        Assert.Equal(shop.Id, sale.ParentId);
        Assert.Equal("_blank", sale.Target);
        Assert.Equal(1, child.DisplayOrder);
    }

    [Fact]
    public async Task SaveNavigationMenu_RemovesItemsNotInIncomingList()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var menu = new NavigationMenu
        {
            Name = "Main Menu",
            Code = "main",
            Description = "Primary",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "system"
        };
        context.NavigationMenus.Add(menu);
        await context.SaveChangesAsync();

        var stale = new NavigationItem { Label = "Old", Url = "/old", DisplayOrder = 0, IsActive = true, MenuId = menu.Id, CreatedAtUtc = DateTime.UtcNow };
        context.NavigationItems.Add(stale);
        await context.SaveChangesAsync();

        var request = new NavigationMenuRequest(
            "Main Menu", "main", "Primary", true,
            new[] { new NavigationItemRequest(null, null, "Only", "/only", null, 0, true) });

        var result = await service.SaveNavigationMenuAsync(menu.Id, request, "admin", CancellationToken.None);

        Assert.True(result.Success);

        var loaded = await service.GetNavigationMenuByCodeAsync("main", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Items);
        Assert.Equal("Only", loaded.Items[0].Label);
    }

    // ---- Policy documents ----

    [Fact]
    public async Task UpdatePolicyDocument_SanitizesBodyAndSetsPublishedDate()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var doc = new PolicyDocument
        {
            Code = "privacy-policy",
            Title = "Privacy Policy",
            Status = ContentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "system"
        };
        context.PolicyDocuments.Add(doc);
        await context.SaveChangesAsync();

        var result = await service.UpdatePolicyDocumentAsync(
            doc.Id,
            new PolicyDocumentRequest("Privacy Policy", "Summary", "<p>Safe</p><script>bad()</script>", ContentStatus.Published),
            "admin",
            CancellationToken.None);

        Assert.True(result.Success);

        var loaded = await service.GetPolicyDocumentByCodeAsync("privacy-policy", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(ContentStatus.Published, loaded.Status);
        Assert.NotNull(loaded.PublishedAtUtc);
        Assert.DoesNotContain("script", loaded.BodyHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ---- System pages cannot be deleted ----

    [Fact]
    public async Task DeletePage_RejectsSystemPage()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var page = new ContentPage
        {
            Title = "About",
            Slug = "about",
            Status = ContentStatus.Published,
            IsSystem = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "system"
        };
        context.ContentPages.Add(page);
        await context.SaveChangesAsync();

        var result = await service.DeletePageAsync(page.Id, "admin", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("System pages cannot be deleted", result.ErrorMessage);
        Assert.Equal(1, await context.ContentPages.CountAsync());
    }
}
