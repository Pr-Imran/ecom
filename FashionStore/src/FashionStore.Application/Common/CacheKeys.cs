namespace FashionStore.Application.Common;

/// <summary>
/// Central registry of distributed cache keys used across services so cache
/// keys stay consistent and cache invalidation remains reliable.
/// </summary>
public static class CacheKeys
{
    public const string HomePage = "homepage:data";

    /// <summary>Published content pages visible on the storefront.</summary>
    public const string ContentPages = "content:pages:published";

    /// <summary>Active banners by placement, cached together.</summary>
    public const string Banners = "content:banners:active";

    /// <summary>Published, in-schedule homepage sections.</summary>
    public const string HomepageSections = "content:homepage-sections:published";

    /// <summary>Active navigation menu by code (cached per code).</summary>
    public const string NavigationMenu = "content:navigation:{code}";

    /// <summary>Active FAQ items grouped by category.</summary>
    public const string FaqItems = "content:faq:active";

    /// <summary>Policy documents by code (cached per code).</summary>
    public const string PolicyDocument = "content:policy:{code}";

    /// <summary>The composed, strongly typed website settings snapshot.</summary>
    public const string WebsiteSettings = "settings:website";

    /// <summary>Published policy documents that render on the storefront.</summary>
    public const string PolicyDocuments = "content:policies:published";
}
