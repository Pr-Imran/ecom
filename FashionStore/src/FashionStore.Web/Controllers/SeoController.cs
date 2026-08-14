using System.Text;
using System.Xml.Linq;
using FashionStore.Application.DTOs.Seo;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace FashionStore.Web.Controllers;

/// <summary>
/// Serves the search-engine endpoints: <c>/robots.txt</c> (crawl directives that
/// keep private areas out of the index and point to the sitemap) and
/// <c>/sitemap.xml</c> (all indexable storefront URLs). Both endpoints are
/// cached by the output cache and the sitemap payload is cached by the sitemap
/// service, so search-engine crawls never hit the database.
/// </summary>
public sealed class SeoController : Controller
{
    private static readonly string[] DisallowedPaths =
    {
        "/account", "/admin", "/addresses", "/cart", "/checkout", "/orders",
        "/order", "/returns", "/reviews", "/wishlist", "/payments", "/hangfire", "/demo"
    };

    private readonly ISitemapService _sitemapService;
    private readonly ILogger<SeoController> _logger;

    public SeoController(ISitemapService sitemapService, ILogger<SeoController> logger)
    {
        _sitemapService = sitemapService;
        _logger = logger;
    }

    [HttpGet("/robots.txt")]
    [OutputCache(Duration = 86400)]
    public IActionResult RobotsTxt()
    {
        var siteUrl = $"{Request.Scheme}://{Request.Host}";
        var builder = new StringBuilder();

        builder.AppendLine("User-agent: *");
        builder.AppendLine("Allow: /");
        foreach (var path in DisallowedPaths)
        {
            builder.AppendLine($"Disallow: {path}");
        }

        builder.AppendLine();
        builder.Append($"Sitemap: {siteUrl}/sitemap.xml");

        return Content(builder.ToString(), "text/plain", Encoding.UTF8);
    }

    [HttpGet("/sitemap.xml")]
    [OutputCache(Duration = 600)]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sitemap(CancellationToken cancellationToken)
    {
        var siteUrl = $"{Request.Scheme}://{Request.Host}";
        SitemapData data;
        try
        {
            data = await _sitemapService.GetSitemapAsync(siteUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build the sitemap.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var urlset = new XElement(ns + "urlset",
            data.Entries.Select(entry => new XElement(ns + "url",
                new XElement(ns + "loc", data.SiteUrl.TrimEnd('/') + entry.Url),
                entry.LastModifiedUtc is { } lastModified
                    ? new XElement(ns + "lastmod", lastModified.ToString("yyyy-MM-dd"))
                    : null,
                entry.ChangeFrequency is { } frequency
                    ? new XElement(ns + "changefreq", frequency)
                    : null,
                entry.Priority is { } priority
                    ? new XElement(ns + "priority", priority.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    : null)));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), urlset);
        var payload = document.ToString(SaveOptions.DisableFormatting);

        return Content(payload, "application/xml", Encoding.UTF8);
    }
}
