namespace FashionStore.Application.Configuration;

/// <summary>
/// Key registry and defaults for the strongly typed website settings persisted
/// in the <c>SiteSettings</c> table. Keys are stable and grouped; defaults are
/// merged with any existing config-bound settings so unset rows fall back to
/// sensible values on first run.
/// </summary>
public static class WebsiteSettingsDefaults
{
    public static class Keys
    {
        public const string StoreName = "store.name";
        public const string Tagline = "store.tagline";
        public const string BusinessRegistration = "store.business_registration";

        public const string LogoUrl = "branding.logo_url";
        public const string FaviconUrl = "branding.favicon_url";
        public const string AccentColour = "branding.accent_colour";
        public const string FacebookUrl = "branding.social_facebook";
        public const string InstagramUrl = "branding.social_instagram";
        public const string TwitterUrl = "branding.social_twitter";
        public const string YouTubeUrl = "branding.social_youtube";

        public const string ContactEmail = "contact.email";
        public const string ContactPhone = "contact.phone";
        public const string Address = "contact.address";

        public const string CurrencyCode = "commerce.currency_code";
        public const string CurrencySymbol = "commerce.currency_symbol";
        public const string Timezone = "commerce.timezone";
        public const string ReturnWindowDays = "commerce.return_window_days";
        public const string InvoicePrefix = "commerce.invoice_prefix";
        public const string LowStockThreshold = "commerce.low_stock_threshold";
        public const string LowStockAlertEmail = "commerce.low_stock_alert_email";

        public const string GuestCheckoutEnabled = "checkout.guest_checkout_enabled";
        public const string RequiresTermsAcceptance = "checkout.requires_terms_acceptance";
        public const string RequireGuestPhone = "checkout.require_guest_phone";

        public const string OrderNumberPrefix = "orders.order_number_prefix";
        public const string ReturnNumberPrefix = "orders.return_number_prefix";

        public const string DefaultMetaTitle = "seo.default_meta_title";
        public const string DefaultMetaDescription = "seo.default_meta_description";

        public const string MaintenanceMode = "maintenance.mode";
        public const string MaintenanceMessage = "maintenance.message";

        public const string AutoApproveReviews = "reviews.auto_approve";
        public const string EnableAnonymousReviews = "reviews.anonymous_enabled";
        public const string MaxImagesPerReview = "reviews.max_images";
    }

    /// <summary>
    /// Keys that only a SuperAdmin may change. Changing currency, timezone,
    /// maintenance mode or business registration affects orders, billing and the
    /// entire storefront, so those writes are restricted by the settings service.
    /// </summary>
    public static readonly string[] ProtectedKeys =
    {
        Keys.BusinessRegistration,
        Keys.CurrencyCode,
        Keys.CurrencySymbol,
        Keys.Timezone,
        Keys.MaintenanceMode,
        Keys.MaintenanceMessage,
        Keys.InvoicePrefix,
        Keys.ReturnNumberPrefix
    };

    public static readonly string[] AllKeys =
    {
        Keys.StoreName, Keys.Tagline, Keys.BusinessRegistration,
        Keys.LogoUrl, Keys.FaviconUrl, Keys.AccentColour,
        Keys.FacebookUrl, Keys.InstagramUrl, Keys.TwitterUrl, Keys.YouTubeUrl,
        Keys.ContactEmail, Keys.ContactPhone, Keys.Address,
        Keys.CurrencyCode, Keys.CurrencySymbol, Keys.Timezone, Keys.ReturnWindowDays,
        Keys.InvoicePrefix, Keys.LowStockThreshold, Keys.LowStockAlertEmail,
        Keys.GuestCheckoutEnabled, Keys.RequiresTermsAcceptance, Keys.RequireGuestPhone,
        Keys.OrderNumberPrefix, Keys.ReturnNumberPrefix,
        Keys.DefaultMetaTitle, Keys.DefaultMetaDescription,
        Keys.MaintenanceMode, Keys.MaintenanceMessage,
        Keys.AutoApproveReviews, Keys.EnableAnonymousReviews, Keys.MaxImagesPerReview
    };

    public static readonly IReadOnlyDictionary<string, string> GroupByKey =
        new Dictionary<string, string>
        {
            [Keys.StoreName] = "store",
            [Keys.Tagline] = "store",
            [Keys.BusinessRegistration] = "store",
            [Keys.LogoUrl] = "branding",
            [Keys.FaviconUrl] = "branding",
            [Keys.AccentColour] = "branding",
            [Keys.FacebookUrl] = "branding",
            [Keys.InstagramUrl] = "branding",
            [Keys.TwitterUrl] = "branding",
            [Keys.YouTubeUrl] = "branding",
            [Keys.ContactEmail] = "contact",
            [Keys.ContactPhone] = "contact",
            [Keys.Address] = "contact",
            [Keys.CurrencyCode] = "commerce",
            [Keys.CurrencySymbol] = "commerce",
            [Keys.Timezone] = "commerce",
            [Keys.ReturnWindowDays] = "commerce",
            [Keys.InvoicePrefix] = "commerce",
            [Keys.LowStockThreshold] = "commerce",
            [Keys.LowStockAlertEmail] = "commerce",
            [Keys.GuestCheckoutEnabled] = "checkout",
            [Keys.RequiresTermsAcceptance] = "checkout",
            [Keys.RequireGuestPhone] = "checkout",
            [Keys.OrderNumberPrefix] = "orders",
            [Keys.ReturnNumberPrefix] = "orders",
            [Keys.DefaultMetaTitle] = "seo",
            [Keys.DefaultMetaDescription] = "seo",
            [Keys.MaintenanceMode] = "maintenance",
            [Keys.MaintenanceMessage] = "maintenance",
            [Keys.AutoApproveReviews] = "reviews",
            [Keys.EnableAnonymousReviews] = "reviews",
            [Keys.MaxImagesPerReview] = "reviews"
        };
}
