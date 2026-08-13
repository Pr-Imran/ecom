using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Settings;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class WebsiteSettingsServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-settings-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static WebsiteSettingsService CreateService(AppDbContext context, IDistributedCache cache)
        => new(
            context,
            cache,
            new CacheSettings { AbsoluteExpirationMinutes = 10 },
            new StoreSettings(),
            NullLogger<WebsiteSettingsService>.Instance);

    private static MemoryDistributedCache CreateCache()
        => new(new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task GetSettingsAsync_ReturnsConfigDefaultsWhenNoRowsExist()
    {
        var context = CreateContext();
        var service = CreateService(context, CreateCache());

        var snapshot = await service.GetSettingsAsync(CancellationToken.None);

        Assert.Equal("FashionStore", snapshot.Store.StoreName);
        Assert.Equal("USD", snapshot.Commerce.CurrencyCode);
        Assert.Equal("$", snapshot.Commerce.CurrencySymbol);
        Assert.True(snapshot.Checkout.GuestCheckoutEnabled);
        Assert.False(snapshot.Maintenance.MaintenanceMode);
    }

    [Fact]
    public async Task GetSettingsAsync_UsesCacheUntilInvalidated()
    {
        var context = CreateContext();
        var cache = CreateCache();
        var service = CreateService(context, cache);

        var first = await service.GetSettingsAsync(CancellationToken.None);

        context.SiteSettings.Add(new SiteSetting
        {
            Key = WebsiteSettingsDefaults.Keys.StoreName,
            Value = "Changed Directly",
            ValueType = "string",
            Group = "store",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();

        var cached = await service.GetSettingsAsync(CancellationToken.None);
        Assert.Equal(first.Store.StoreName, cached.Store.StoreName);
        Assert.NotEqual("Changed Directly", cached.Store.StoreName);

        await service.InvalidateSettingsCacheAsync(CancellationToken.None);
        var fresh = await service.GetSettingsAsync(CancellationToken.None);
        Assert.Equal("Changed Directly", fresh.Store.StoreName);
    }

    [Fact]
    public async Task UpdateSettingsAsync_RejectsProtectedKeysForNonSuperAdmin()
    {
        var context = CreateContext();
        var service = CreateService(context, CreateCache());

        var request = new UpdateWebsiteSettingsRequest(
            Store: new StoreSection("New Name", "Tagline", "REG-123"),
            Branding: null,
            Contact: null,
            Commerce: null,
            Checkout: null,
            Orders: null,
            Seo: null,
            Maintenance: null,
            Reviews: null);

        var result = await service.UpdateSettingsAsync(request, "admin", isSuperAdmin: false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("protected", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await context.SiteSettings.CountAsync());
    }

    [Fact]
    public async Task UpdateSettingsAsync_SuperAdminCanChangeProtectedKeys()
    {
        var context = CreateContext();
        var service = CreateService(context, CreateCache());

        var request = new UpdateWebsiteSettingsRequest(
            Store: new StoreSection("New Name", "Tagline", "REG-123"),
            Branding: null,
            Contact: null,
            Commerce: new CommerceSection("EUR", "€", "Europe/Berlin", 45, "INV-", 3, "alerts@example.com"),
            Checkout: null,
            Orders: null,
            Seo: null,
            Maintenance: null,
            Reviews: null);

        var result = await service.UpdateSettingsAsync(request, "superadmin", isSuperAdmin: true, CancellationToken.None);

        Assert.True(result.Success);

        var snapshot = await service.GetSettingsAsync(CancellationToken.None);
        Assert.Equal("New Name", snapshot.Store.StoreName);
        Assert.Equal("REG-123", snapshot.Store.BusinessRegistration);
        Assert.Equal("EUR", snapshot.Commerce.CurrencyCode);
        Assert.Equal("€", snapshot.Commerce.CurrencySymbol);
        Assert.Equal(45, snapshot.Commerce.ReturnWindowDays);
    }

    [Fact]
    public async Task UpdateSettingsAsync_DoesNotChangeUnrelatedSections()
    {
        var context = CreateContext();
        var service = CreateService(context, CreateCache());

        var request = new UpdateWebsiteSettingsRequest(
            Store: null,
            Branding: new BrandingSection("https://example.com/logo.png", "", "#ff0000", "", "", "", ""),
            Contact: null,
            Commerce: null,
            Checkout: null,
            Orders: null,
            Seo: null,
            Maintenance: null,
            Reviews: null);

        var result = await service.UpdateSettingsAsync(request, "admin", isSuperAdmin: false, CancellationToken.None);

        Assert.True(result.Success);

        var snapshot = await service.GetSettingsAsync(CancellationToken.None);
        Assert.Equal("FashionStore", snapshot.Store.StoreName);
        Assert.Equal("https://example.com/logo.png", snapshot.Branding.LogoUrl);
        Assert.Equal("#ff0000", snapshot.Branding.AccentColour);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WritesAuditEntry()
    {
        var context = CreateContext();
        var service = CreateService(context, CreateCache());

        var request = new UpdateWebsiteSettingsRequest(
            Store: null,
            Branding: new BrandingSection("https://example.com/logo.png", "", "#000000", "", "", "", ""),
            Contact: null,
            Commerce: null,
            Checkout: null,
            Orders: null,
            Seo: null,
            Maintenance: null,
            Reviews: null);

        var result = await service.UpdateSettingsAsync(request, "admin-user-1", isSuperAdmin: false, CancellationToken.None);

        Assert.True(result.Success);

        var audit = await context.AuditLogs.SingleOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("Settings.Update", audit.Action);
        Assert.Equal("SiteSetting", audit.EntityType);
        Assert.Equal("admin-user-1", audit.UserId);
        Assert.Contains("branding.logo_url", audit.NewValue);
    }

    [Fact]
    public async Task UpdateSettingsAsync_InvalidatesCache()
    {
        var context = CreateContext();
        var cache = CreateCache();
        var service = CreateService(context, cache);

        await service.GetSettingsAsync(CancellationToken.None);
        Assert.NotNull(await cache.GetStringAsync(CacheKeys.WebsiteSettings));

        var request = new UpdateWebsiteSettingsRequest(
            Store: null,
            Branding: new BrandingSection("https://example.com/logo.png", "", "#000000", "", "", "", ""),
            Contact: null,
            Commerce: null,
            Checkout: null,
            Orders: null,
            Seo: null,
            Maintenance: null,
            Reviews: null);

        await service.UpdateSettingsAsync(request, "admin", isSuperAdmin: false, CancellationToken.None);

        Assert.Null(await cache.GetStringAsync(CacheKeys.WebsiteSettings));
    }

    [Fact]
    public async Task UpdateSettingsAsync_NoChangeDoesNotWriteAudit()
    {
        var context = CreateContext();
        var service = CreateService(context, CreateCache());

        var now = DateTime.UtcNow;
        var logo = "https://example.com/logo.png";
        var empty = string.Empty;
        var seeds = new[]
        {
            new SiteSetting { Key = WebsiteSettingsDefaults.Keys.LogoUrl, Value = logo, ValueType = "string", Group = "branding", CreatedAtUtc = now, CreatedBy = "system" },
            new SiteSetting { Key = WebsiteSettingsDefaults.Keys.FaviconUrl, Value = empty, ValueType = "string", Group = "branding", CreatedAtUtc = now, CreatedBy = "system" },
            new SiteSetting { Key = WebsiteSettingsDefaults.Keys.AccentColour, Value = "#000000", ValueType = "string", Group = "branding", CreatedAtUtc = now, CreatedBy = "system" },
            new SiteSetting { Key = WebsiteSettingsDefaults.Keys.FacebookUrl, Value = empty, ValueType = "string", Group = "branding", CreatedAtUtc = now, CreatedBy = "system" },
            new SiteSetting { Key = WebsiteSettingsDefaults.Keys.InstagramUrl, Value = empty, ValueType = "string", Group = "branding", CreatedAtUtc = now, CreatedBy = "system" },
            new SiteSetting { Key = WebsiteSettingsDefaults.Keys.TwitterUrl, Value = empty, ValueType = "string", Group = "branding", CreatedAtUtc = now, CreatedBy = "system" },
            new SiteSetting { Key = WebsiteSettingsDefaults.Keys.YouTubeUrl, Value = empty, ValueType = "string", Group = "branding", CreatedAtUtc = now, CreatedBy = "system" }
        };
        context.SiteSettings.AddRange(seeds);
        await context.SaveChangesAsync();

        var request = new UpdateWebsiteSettingsRequest(
            Store: null,
            Branding: new BrandingSection("https://example.com/logo.png", "", "#000000", "", "", "", ""),
            Contact: null,
            Commerce: null,
            Checkout: null,
            Orders: null,
            Seo: null,
            Maintenance: null,
            Reviews: null);

        var result = await service.UpdateSettingsAsync(request, "admin", isSuperAdmin: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(7, await context.SiteSettings.CountAsync());
        Assert.Equal(0, await context.AuditLogs.CountAsync());
    }
}
