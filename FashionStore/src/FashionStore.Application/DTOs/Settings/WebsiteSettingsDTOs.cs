namespace FashionStore.Application.DTOs.Settings;

/// <summary>
/// Strongly typed website settings surfaced to the admin settings page. Values
/// are persisted as JSON key/value rows (<c>SiteSetting</c>) and composed into
/// this snapshot by <see cref="IWebsiteSettingsService"/>. The snapshot is cached
/// and invalidated after every update so storefront reads never see stale values.
/// </summary>
public sealed record WebsiteSettingsSnapshot(
    StoreSection Store,
    BrandingSection Branding,
    ContactSection Contact,
    CommerceSection Commerce,
    CheckoutSection Checkout,
    OrderSection Orders,
    SeoSettingsSection Seo,
    MaintenanceSection Maintenance,
    ReviewsSection Reviews);

public sealed record StoreSection(
    string StoreName,
    string Tagline,
    string BusinessRegistration);

public sealed record BrandingSection(
    string LogoUrl,
    string FaviconUrl,
    string AccentColour,
    string FacebookUrl,
    string InstagramUrl,
    string TwitterUrl,
    string YouTubeUrl);

public sealed record ContactSection(
    string ContactEmail,
    string ContactPhone,
    string Address);

public sealed record CommerceSection(
    string CurrencyCode,
    string CurrencySymbol,
    string Timezone,
    int ReturnWindowDays,
    string InvoicePrefix,
    int LowStockThreshold,
    string LowStockAlertEmail);

public sealed record CheckoutSection(
    bool GuestCheckoutEnabled,
    bool RequiresTermsAcceptance,
    bool RequireGuestPhone);

public sealed record OrderSection(
    string OrderNumberPrefix,
    string ReturnNumberPrefix);

public sealed record SeoSettingsSection(
    string DefaultMetaTitle,
    string DefaultMetaDescription);

public sealed record MaintenanceSection(
    bool MaintenanceMode,
    string MaintenanceMessage);

public sealed record ReviewsSection(
    bool AutoApproveReviews,
    bool EnableAnonymousReviews,
    int MaxImagesPerReview);

/// <summary>Payload for updating website settings. Null fields are left unchanged.</summary>
public sealed record UpdateWebsiteSettingsRequest(
    StoreSection? Store,
    BrandingSection? Branding,
    ContactSection? Contact,
    CommerceSection? Commerce,
    CheckoutSection? Checkout,
    OrderSection? Orders,
    SeoSettingsSection? Seo,
    MaintenanceSection? Maintenance,
    ReviewsSection? Reviews);

public sealed record SettingsMutationResult(bool Success, string? ErrorMessage = null);
