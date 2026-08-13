namespace FashionStore.Domain.Enums;

/// <summary>
/// Lifecycle of content records (pages, banners, homepage sections, FAQs and
/// policy documents). Draft content is never visible on the storefront;
/// Published content is visible subject to its schedule window; Archived content
/// is retained for audit purposes but hidden everywhere.
/// </summary>
public enum ContentStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

/// <summary>
/// The layout template used to render a content page. <c>Default</c> renders the
/// standard page shell with breadcrumbs; <c>FullWidth</c> renders the raw body
/// without the padded container so rich landing pages can span the viewport.
/// </summary>
public enum ContentPageTemplate
{
    Default = 0,
    FullWidth = 1
}

/// <summary>
/// Where a banner should be rendered on the storefront. Homepage banners appear
/// in the promo banner rail; the announcement banner sits at the very top of
/// every page.
/// </summary>
public enum BannerPlacement
{
    Homepage = 0,
    Announcement = 1
}
