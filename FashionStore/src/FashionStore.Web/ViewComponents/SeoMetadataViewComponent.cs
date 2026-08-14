using System.Text.Json;
using FashionStore.Application.DTOs.Settings;
using FashionStore.Application.Interfaces;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.ViewComponents;

/// <summary>
/// Renders the storefront <c>&lt;head&gt;</c> SEO metadata for the public
/// layouts: dynamic page title (store name from website settings), meta
/// description, canonical URL, Open Graph + Twitter social tags, favicon,
/// robots no-index rules for private areas, pagination prev/next links and
/// any structured data registered through ViewData.
///
/// ViewData contract:
///   Title                 - page title (store name appended by the component)
///   MetaDescription       - page meta description
///   CanonicalUrl          - canonical URL for this page
///   OgType                - Open Graph type (default "website")
///   SeoImage              - social share image (absolute URL)
///   Robots                - explicit robots directive (overrides defaults)
///   JsonLd                - IEnumerable&lt;string&gt; of pre-serialized JSON-LD scripts
///   PrevPageUrl / NextPageUrl - pagination link rel values
///   IncludeOrganizationSchema - when true, emits Organization + WebSite schemas
///
/// Private areas (admin, account, cart, checkout, orders, returns, reviews,
/// wishlist, payments, demo) always render noindex/nofollow. Settings lookups
/// are cached; on failure the component degrades to static defaults so the
/// storefront never breaks because of a settings outage.
/// </summary>
public sealed class SeoMetadataViewComponent : ViewComponent
{
    private static readonly string[] NoIndexPrefixes =
    {
        "/admin", "/account", "/addresses", "/cart", "/checkout", "/orders",
        "/order", "/returns", "/reviews", "/wishlist", "/payments", "/hangfire", "/demo"
    };

    private readonly IWebsiteSettingsService _settingsService;
    private readonly ILogger<SeoMetadataViewComponent> _logger;

    public SeoMetadataViewComponent(
        IWebsiteSettingsService settingsService,
        ILogger<SeoMetadataViewComponent> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var siteUrl = $"{Request.Scheme}://{Request.Host}";
        var storeName = "FashionStore";
        var defaultMetaDescription = string.Empty;
        string? favicon = null;

        try
        {
            WebsiteSettingsSnapshot? settings = await _settingsService.GetSettingsAsync(CancellationToken.None);
            if (settings is not null)
            {
                storeName = string.IsNullOrWhiteSpace(settings.Store.StoreName) ? storeName : settings.Store.StoreName;
                defaultMetaDescription = settings.Seo.DefaultMetaDescription ?? string.Empty;
                favicon = string.IsNullOrWhiteSpace(settings.Branding.FaviconUrl) ? null : settings.Branding.FaviconUrl;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeoMetadataViewComponent could not read website settings; using defaults.");
        }

        var viewData = ViewData;
        var title = viewData["Title"] as string;
        var fullTitle = string.IsNullOrWhiteSpace(title) ? storeName : $"{title} - {storeName}";

        var description = viewData["MetaDescription"] as string;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = defaultMetaDescription;
        }

        var canonical = viewData["CanonicalUrl"] as string;
        canonical = NormalizeUrl(canonical, siteUrl);

        var robots = viewData["Robots"] as string;
        var isNoIndexArea = IsNoIndexArea(Request.Path.Value);
        if (isNoIndexArea)
        {
            robots = "noindex, nofollow";
        }

        var jsonLd = new List<string>();
        if (viewData["JsonLd"] is IEnumerable<string> custom)
        {
            jsonLd.AddRange(custom.Where(j => !string.IsNullOrWhiteSpace(j)));
        }

        if (viewData["IncludeOrganizationSchema"] is true)
        {
            jsonLd.AddRange(BuildOrganizationSchemas(siteUrl, storeName, favicon));
        }

        var model = new SeoMetadataViewModel(
            FullTitle: fullTitle,
            MetaDescription: string.IsNullOrWhiteSpace(description) ? null : description,
            CanonicalUrl: canonical,
            OgType: viewData["OgType"] as string ?? "website",
            OgImage: NormalizeUrl(viewData["SeoImage"] as string, siteUrl),
            Robots: robots,
            PrevPageUrl: NormalizeUrl(viewData["PrevPageUrl"] as string, siteUrl),
            NextPageUrl: NormalizeUrl(viewData["NextPageUrl"] as string, siteUrl),
            FaviconUrl: favicon,
            JsonLd: jsonLd);

        return View(model);
    }

    private static bool IsNoIndexArea(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return NoIndexPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeUrl(string? url, string siteUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : siteUrl + url;
    }

    private static string[] BuildOrganizationSchemas(string siteUrl, string storeName, string? favicon)
    {
        var organization = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Organization",
            ["name"] = storeName,
            ["url"] = siteUrl,
            ["logo"] = favicon ?? $"{siteUrl}/favicon.ico"
        });

        var webSite = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "WebSite",
            ["name"] = storeName,
            ["url"] = siteUrl,
            ["potentialAction"] = new Dictionary<string, object?>
            {
                ["@type"] = "SearchAction",
                ["target"] = new Dictionary<string, object?>
                {
                    ["@type"] = "EntryPoint",
                    ["urlTemplate"] = $"{siteUrl}/products/search?q={{search_term_string}}"
                },
                ["query-input"] = "required name=search_term_string"
            }
        });

        return new[] { organization, webSite };
    }
}
