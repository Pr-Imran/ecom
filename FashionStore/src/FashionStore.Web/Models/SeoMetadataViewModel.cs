namespace FashionStore.Web.Models;

/// <summary>
/// The SEO metadata payload rendered into the public layouts by the
/// <c>SeoMetadataViewComponent</c>. All URL values are absolute.
/// </summary>
public sealed record SeoMetadataViewModel(
    string FullTitle,
    string? MetaDescription,
    string? CanonicalUrl,
    string OgType,
    string? OgImage,
    string? Robots,
    string? PrevPageUrl,
    string? NextPageUrl,
    string? FaviconUrl,
    IReadOnlyList<string> JsonLd);
