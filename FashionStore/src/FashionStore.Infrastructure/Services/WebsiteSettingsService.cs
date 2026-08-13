using System.Text.Json;
using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Settings;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Reads and writes the strongly typed store-wide settings. Reads compose the
/// <see cref="WebsiteSettingsSnapshot"/> from the <c>SiteSettings</c> table
/// (falling back to config-driven defaults for unset rows) and cache the result;
/// writes persist the changed keys, reject protected keys for non-SuperAdmins,
/// audit the change and invalidate the settings cache.
/// </summary>
public sealed class WebsiteSettingsService : IWebsiteSettingsService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly CacheSettings _cacheSettings;
    private readonly StoreSettings _storeSettings;
    private readonly ILogger<WebsiteSettingsService> _logger;

    public WebsiteSettingsService(
        AppDbContext context,
        IDistributedCache cache,
        CacheSettings cacheSettings,
        StoreSettings storeSettings,
        ILogger<WebsiteSettingsService> logger)
    {
        _context = context;
        _cache = cache;
        _cacheSettings = cacheSettings;
        _storeSettings = storeSettings;
        _logger = logger;
    }

    public async Task<WebsiteSettingsSnapshot> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.WebsiteSettings, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<WebsiteSettingsSnapshot>(cached)!;
        }

        var rows = await _context.SiteSettings.AsNoTracking().ToListAsync(cancellationToken);
        var values = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        var snapshot = ComposeSnapshot(values);

        await _cache.SetStringAsync(CacheKeys.WebsiteSettings, JsonSerializer.Serialize(snapshot), GetCacheOptions(), cancellationToken);
        return snapshot;
    }

    public async Task<SettingsMutationResult> UpdateSettingsAsync(
        UpdateWebsiteSettingsRequest request,
        string actorId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return new SettingsMutationResult(false, "No settings supplied.");
        }

        var rows = await _context.SiteSettings.ToListAsync(cancellationToken);
        var byKey = rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);

        var updates = BuildUpdates(request);

        // Reject protected-key writes from non-SuperAdmin callers up front.
        if (!isSuperAdmin)
        {
            var protectedHit = updates.Keys.FirstOrDefault(k => WebsiteSettingsDefaults.ProtectedKeys.Contains(k));
            if (protectedHit is not null)
            {
                return new SettingsMutationResult(false,
                    $"The setting \"{protectedHit}\" is protected and can only be changed by a SuperAdmin.");
            }
        }

        var now = DateTime.UtcNow;
        var changed = new List<string>();

        foreach (var (key, value) in updates)
        {
            if (byKey.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.Value, value, StringComparison.Ordinal))
                {
                    existing.Value = value;
                    existing.UpdatedAtUtc = now;
                    existing.UpdatedBy = actorId;
                    changed.Add(key);
                }
            }
            else
            {
                _context.SiteSettings.Add(new SiteSetting
                {
                    Key = key,
                    Value = value,
                    ValueType = ValueTypeForKey(key),
                    Group = WebsiteSettingsDefaults.GroupByKey.GetValueOrDefault(key),
                    IsProtected = WebsiteSettingsDefaults.ProtectedKeys.Contains(key),
                    CreatedAtUtc = now,
                    CreatedBy = actorId
                });
                changed.Add(key);
            }
        }

        if (changed.Count == 0)
        {
            return new SettingsMutationResult(true, null);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(actorId, changed, cancellationToken);
        await InvalidateSettingsCacheAsync(cancellationToken);

        return new SettingsMutationResult(true, null);
    }

    public async Task InvalidateSettingsCacheAsync(CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(CacheKeys.WebsiteSettings, cancellationToken);
    }

    private WebsiteSettingsSnapshot ComposeSnapshot(IReadOnlyDictionary<string, string> values)
    {
        return new WebsiteSettingsSnapshot(
            new StoreSection(
                GetString(values, WebsiteSettingsDefaults.Keys.StoreName, _storeSettings.Name),
                GetString(values, WebsiteSettingsDefaults.Keys.Tagline, _storeSettings.Tagline),
                GetString(values, WebsiteSettingsDefaults.Keys.BusinessRegistration, string.Empty)),
            new BrandingSection(
                GetString(values, WebsiteSettingsDefaults.Keys.LogoUrl, string.Empty),
                GetString(values, WebsiteSettingsDefaults.Keys.FaviconUrl, string.Empty),
                GetString(values, WebsiteSettingsDefaults.Keys.AccentColour, string.Empty),
                GetString(values, WebsiteSettingsDefaults.Keys.FacebookUrl, string.Empty),
                GetString(values, WebsiteSettingsDefaults.Keys.InstagramUrl, string.Empty),
                GetString(values, WebsiteSettingsDefaults.Keys.TwitterUrl, string.Empty),
                GetString(values, WebsiteSettingsDefaults.Keys.YouTubeUrl, string.Empty)),
            new ContactSection(
                GetString(values, WebsiteSettingsDefaults.Keys.ContactEmail, _storeSettings.ContactEmail),
                GetString(values, WebsiteSettingsDefaults.Keys.ContactPhone, _storeSettings.ContactPhone),
                GetString(values, WebsiteSettingsDefaults.Keys.Address, _storeSettings.Address)),
            new CommerceSection(
                GetString(values, WebsiteSettingsDefaults.Keys.CurrencyCode, _storeSettings.CurrencyCode),
                GetString(values, WebsiteSettingsDefaults.Keys.CurrencySymbol, _storeSettings.CurrencySymbol),
                GetString(values, WebsiteSettingsDefaults.Keys.Timezone, "UTC"),
                GetInt(values, WebsiteSettingsDefaults.Keys.ReturnWindowDays, 30),
                GetString(values, WebsiteSettingsDefaults.Keys.InvoicePrefix, "INV-"),
                GetInt(values, WebsiteSettingsDefaults.Keys.LowStockThreshold, 5),
                GetString(values, WebsiteSettingsDefaults.Keys.LowStockAlertEmail, _storeSettings.ContactEmail)),
            new CheckoutSection(
                GetBool(values, WebsiteSettingsDefaults.Keys.GuestCheckoutEnabled, true),
                GetBool(values, WebsiteSettingsDefaults.Keys.RequiresTermsAcceptance, true),
                GetBool(values, WebsiteSettingsDefaults.Keys.RequireGuestPhone, true)),
            new OrderSection(
                GetString(values, WebsiteSettingsDefaults.Keys.OrderNumberPrefix, "ORD"),
                GetString(values, WebsiteSettingsDefaults.Keys.ReturnNumberPrefix, "RMA")),
            new SeoSettingsSection(
                GetString(values, WebsiteSettingsDefaults.Keys.DefaultMetaTitle, string.Empty),
                GetString(values, WebsiteSettingsDefaults.Keys.DefaultMetaDescription, string.Empty)),
            new MaintenanceSection(
                GetBool(values, WebsiteSettingsDefaults.Keys.MaintenanceMode, false),
                GetString(values, WebsiteSettingsDefaults.Keys.MaintenanceMessage, "We'll be back soon.")),
            new ReviewsSection(
                GetBool(values, WebsiteSettingsDefaults.Keys.AutoApproveReviews, false),
                GetBool(values, WebsiteSettingsDefaults.Keys.EnableAnonymousReviews, false),
                GetInt(values, WebsiteSettingsDefaults.Keys.MaxImagesPerReview, 6)));
    }

    private static Dictionary<string, string> BuildUpdates(UpdateWebsiteSettingsRequest request)
    {
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (request.Store is { } store)
        {
            updates[WebsiteSettingsDefaults.Keys.StoreName] = store.StoreName;
            updates[WebsiteSettingsDefaults.Keys.Tagline] = store.Tagline;
            updates[WebsiteSettingsDefaults.Keys.BusinessRegistration] = store.BusinessRegistration;
        }

        if (request.Branding is { } branding)
        {
            updates[WebsiteSettingsDefaults.Keys.LogoUrl] = branding.LogoUrl;
            updates[WebsiteSettingsDefaults.Keys.FaviconUrl] = branding.FaviconUrl;
            updates[WebsiteSettingsDefaults.Keys.AccentColour] = branding.AccentColour;
            updates[WebsiteSettingsDefaults.Keys.FacebookUrl] = branding.FacebookUrl;
            updates[WebsiteSettingsDefaults.Keys.InstagramUrl] = branding.InstagramUrl;
            updates[WebsiteSettingsDefaults.Keys.TwitterUrl] = branding.TwitterUrl;
            updates[WebsiteSettingsDefaults.Keys.YouTubeUrl] = branding.YouTubeUrl;
        }

        if (request.Contact is { } contact)
        {
            updates[WebsiteSettingsDefaults.Keys.ContactEmail] = contact.ContactEmail;
            updates[WebsiteSettingsDefaults.Keys.ContactPhone] = contact.ContactPhone;
            updates[WebsiteSettingsDefaults.Keys.Address] = contact.Address;
        }

        if (request.Commerce is { } commerce)
        {
            updates[WebsiteSettingsDefaults.Keys.CurrencyCode] = commerce.CurrencyCode;
            updates[WebsiteSettingsDefaults.Keys.CurrencySymbol] = commerce.CurrencySymbol;
            updates[WebsiteSettingsDefaults.Keys.Timezone] = commerce.Timezone;
            updates[WebsiteSettingsDefaults.Keys.ReturnWindowDays] = commerce.ReturnWindowDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
            updates[WebsiteSettingsDefaults.Keys.InvoicePrefix] = commerce.InvoicePrefix;
            updates[WebsiteSettingsDefaults.Keys.LowStockThreshold] = commerce.LowStockThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            updates[WebsiteSettingsDefaults.Keys.LowStockAlertEmail] = commerce.LowStockAlertEmail;
        }

        if (request.Checkout is { } checkout)
        {
            updates[WebsiteSettingsDefaults.Keys.GuestCheckoutEnabled] = checkout.GuestCheckoutEnabled.ToString();
            updates[WebsiteSettingsDefaults.Keys.RequiresTermsAcceptance] = checkout.RequiresTermsAcceptance.ToString();
            updates[WebsiteSettingsDefaults.Keys.RequireGuestPhone] = checkout.RequireGuestPhone.ToString();
        }

        if (request.Orders is { } orders)
        {
            updates[WebsiteSettingsDefaults.Keys.OrderNumberPrefix] = orders.OrderNumberPrefix;
            updates[WebsiteSettingsDefaults.Keys.ReturnNumberPrefix] = orders.ReturnNumberPrefix;
        }

        if (request.Seo is { } seo)
        {
            updates[WebsiteSettingsDefaults.Keys.DefaultMetaTitle] = seo.DefaultMetaTitle;
            updates[WebsiteSettingsDefaults.Keys.DefaultMetaDescription] = seo.DefaultMetaDescription;
        }

        if (request.Maintenance is { } maintenance)
        {
            updates[WebsiteSettingsDefaults.Keys.MaintenanceMode] = maintenance.MaintenanceMode.ToString();
            updates[WebsiteSettingsDefaults.Keys.MaintenanceMessage] = maintenance.MaintenanceMessage;
        }

        if (request.Reviews is { } reviews)
        {
            updates[WebsiteSettingsDefaults.Keys.AutoApproveReviews] = reviews.AutoApproveReviews.ToString();
            updates[WebsiteSettingsDefaults.Keys.EnableAnonymousReviews] = reviews.EnableAnonymousReviews.ToString();
            updates[WebsiteSettingsDefaults.Keys.MaxImagesPerReview] = reviews.MaxImagesPerReview.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return updates;
    }

    private async Task WriteAuditAsync(string actorId, IReadOnlyList<string> changedKeys, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = user?.Id ?? actorId,
                Action = "Settings.Update",
                EntityType = "SiteSetting",
                NewValue = JsonSerializer.Serialize(changedKeys.OrderBy(k => k)),
                IpAddress = string.Empty,
                UserAgent = string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write settings audit entry for actor {ActorId}", actorId);
        }
    }

    private static string ValueTypeForKey(string key) => key.EndsWith("_enabled", StringComparison.Ordinal) || key.Contains(".mode", StringComparison.Ordinal) ? "boolean" : "string";

    private static string GetString(IReadOnlyDictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
    {
        if (values.TryGetValue(key, out var value) && bool.TryParse(value, out var result))
        {
            return result;
        }

        return fallback;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
    {
        if (values.TryGetValue(key, out var value) && int.TryParse(value, out var result))
        {
            return result;
        }

        return fallback;
    }

    private DistributedCacheEntryOptions GetCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes),
        SlidingExpiration = TimeSpan.FromMinutes(_cacheSettings.SlidingExpirationMinutes)
    };
}
