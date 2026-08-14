using System.Net;
using System.Net.Http.Headers;
using FashionStore.Application.DTOs.Seo;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Integration coverage for the Phase 29 SEO, accessibility and performance
/// work: robots.txt, the XML sitemap, structured data, slug redirects, static
/// asset caching and response compression.
/// </summary>
public class SeoAndPerformanceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SeoAndPerformanceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    // ---- robots.txt ----

    [Fact]
    public async Task RobotsTxt_ReturnsPlainText_AndPointsToSitemap()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("User-agent: *", body);
        Assert.Contains("Sitemap: ", body);
        Assert.EndsWith("/sitemap.xml", body.Trim());
    }

    [Fact]
    public async Task RobotsTxt_DisallowsPrivateAreas()
    {
        var client = CreateClient();
        var body = await client.GetStringAsync("/robots.txt");

        Assert.Contains("Disallow: /admin", body);
        Assert.Contains("Disallow: /account", body);
        Assert.Contains("Disallow: /checkout", body);
        Assert.Contains("Disallow: /cart", body);
    }

    // ---- sitemap ----

    [Fact]
    public async Task SitemapXml_ReturnsXml_WithSeededCatalogueAndPages()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("http://www.sitemaps.org/schemas/sitemap/0.9", body);
        Assert.Contains("/products/cashmere-crew-neck-sweater", body);
        Assert.Contains("/categories/clothing", body);
        Assert.Contains("/brands/everlane", body);
        Assert.Contains("/collections/autumn-edit", body);
    }

    [Fact]
    public async Task SitemapXml_HasNoDuplicateUrls()
    {
        var client = CreateClient();
        var body = await client.GetStringAsync("/sitemap.xml");

        var urls = body.Split("<loc>", StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split("</loc>").First())
            .Where(u => u.StartsWith("http", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(urls.Count, urls.Distinct().Count());
    }

    // ---- structured data ----

    [Fact]
    public async Task HomePage_ContainsOrganizationAndWebsiteSchema()
    {
        var client = CreateClient();
        var body = await client.GetStringAsync("/");

        Assert.Contains("application/ld+json", body);
        Assert.Contains("\"@type\":\"Organization\"", body.Replace(" ", ""));
        Assert.Contains("\"@type\":\"WebSite\"", body.Replace(" ", ""));
    }

    [Fact]
    public async Task ProductDetails_ContainsBreadcrumbSchema()
    {
        var client = CreateClient();
        var body = await client.GetStringAsync("/products/cashmere-crew-neck-sweater");

        Assert.Contains("\"@type\":\"BreadcrumbList\"", body.Replace(" ", ""));
        Assert.Contains("/categories/clothing", body);
    }

    [Fact]
    public async Task ProductListing_EmitsCanonicalUrl()
    {
        var client = CreateClient();
        var body = await client.GetStringAsync("/products");

        Assert.Contains("rel=\"canonical\"", body);
        Assert.Contains("application/ld+json", body);
    }

    // ---- accessibility ----

    [Fact]
    public async Task PublicPages_HaveSkipLinkAndMainContentTarget()
    {
        var client = CreateClient();
        var body = await client.GetStringAsync("/");

        Assert.Contains("skip-link", body);
        Assert.True(
            body.Contains("id=\"main-content\"", StringComparison.Ordinal) ||
            body.Contains("id=\"public-main-content\"", StringComparison.Ordinal),
            "Expected a main content landmark targeted by the skip link.");
    }

    // ---- slug redirects ----

    [Fact]
    public async Task ProductSlugRedirect_Returns301ToNewSlug()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ISlugRedirectService>();
        await service.AddOrUpdateAsync(new SlugRedirectRequest(
            SlugEntityType.Product,
            "cashmere-sweater-old",
            "cashmere-crew-neck-sweater"));

        var client = CreateClient();
        var response = await client.GetAsync("/products/cashmere-sweater-old");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/products/cashmere-crew-neck-sweater", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task UnknownProductSlug_WithNoRedirect_Returns404()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products/never-existed");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- performance: caching & compression ----

    [Fact]
    public async Task StaticAssets_HaveCacheControlHeaders()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/css/site.min.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Public == true);
        Assert.NotNull(response.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task HtmlResponses_AreGzipCompressed_WhenAccepted()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Content.Headers.ContentEncoding, e => e == "gzip");
    }
}
