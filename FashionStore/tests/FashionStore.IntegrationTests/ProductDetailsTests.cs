using System.Net;
using System.Text;
using System.Text.Json;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

public class ProductDetailsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductDetailsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private (Guid productId, Guid variantId) GetIds(string productSlug, string variantSku)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = db.Products.Single(p => p.Slug == productSlug);
        var variant = db.ProductVariants.Single(v => v.Sku == variantSku);
        return (product.Id, variant.Id);
    }

    [Theory]
    [InlineData("/products/cashmere-crew-neck-sweater")]
    public async Task Details_WithValidSlug_ReturnsOk(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Details_WithUnknownSlug_ReturnsNotFound()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products/unknown-product");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Details_ContainsStructuredData()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products/cashmere-crew-neck-sweater");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("application/ld+json", content);
        Assert.Contains("Product", content);
        Assert.Contains("BreadcrumbList", content);
    }

    [Fact]
    public async Task Details_ContainsProductInformation()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products/cashmere-crew-neck-sweater");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("Cashmere Crew Neck Sweater", content);
        Assert.Contains("Cashmere", content);
        Assert.Contains("Size Guide", content);
        Assert.Contains("Delivery", content);
        Assert.Contains("You May Also Like", content);
    }

    [Fact]
    public async Task Details_SetsRecentlyViewedCookie()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products/cashmere-crew-neck-sweater");

        var cookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("fashionstore_recently_viewed"));
        Assert.NotNull(cookie);
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddToCart_WithValidVariant_ReturnsServerComputedItem()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/products/cashmere-crew-neck-sweater");
        var token = ExtractAntiforgeryToken(html);

        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");
        var payload = JsonSerializer.Serialize(new
        {
            productId,
            variantId,
            quantity = 1
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/products/add-to-cart")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(variantId.ToString(), doc.RootElement.GetProperty("item").GetProperty("variantId").GetString());
        Assert.Equal("SW-1001-GREY-M", doc.RootElement.GetProperty("item").GetProperty("variantSku").GetString());
    }

    [Fact]
    public async Task AddToCart_WithOutOfStockVariant_ReturnsError()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/products/trail-running-shoe");
        var token = ExtractAntiforgeryToken(html);

        var (productId, variantId) = GetIds("trail-running-shoe", "SH-3003-BLK-09");
        var payload = JsonSerializer.Serialize(new
        {
            productId,
            variantId,
            quantity = 1
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/products/add-to-cart")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("stock", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AddToCart_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");
        var payload = JsonSerializer.Serialize(new
        {
            productId,
            variantId,
            quantity = 1
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/products/add-to-cart")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(
                html,
                @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""");
        }
        Assert.True(match.Success, "Antiforgery token not found in HTML");
        return match.Groups[1].Value;
    }
}
