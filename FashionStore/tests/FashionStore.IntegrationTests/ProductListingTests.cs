using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FashionStore.IntegrationTests;

public class ProductListingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductListingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("/products")]
    [InlineData("/products?sort=price-asc")]
    [InlineData("/products?sort=price-desc")]
    [InlineData("/products?sort=newest")]
    [InlineData("/products?sort=rating")]
    [InlineData("/products?sort=discount")]
    [InlineData("/products?category=clothing")]
    [InlineData("/products?brand=everlane")]
    [InlineData("/products?in-stock=true")]
    [InlineData("/products?min-rating=4")]
    [InlineData("/products?page=2")]
    public async Task ProductListing_ReturnsOk(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/products/search?q=sweater")]
    [InlineData("/products/search?q=SW-1001")]
    [InlineData("/products/search?q=cashmere")]
    public async Task Search_WithQuery_ReturnsOk(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithoutQuery_RedirectsToIndex()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products/search");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/products", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/products/sale")]
    [InlineData("/products/new")]
    [InlineData("/products/best")]
    [InlineData("/categories/clothing")]
    [InlineData("/brands/everlane")]
    [InlineData("/collections/autumn-edit")]
    public async Task ListingRoutes_ReturnOk(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/categories/unknown-slug")]
    [InlineData("/brands/unknown-slug")]
    [InlineData("/collections/unknown-slug")]
    public async Task ListingRoutes_UnknownSlug_ReturnsNotFound(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/categories")]
    [InlineData("/collections")]
    public async Task ViewAllRoutes_RedirectToProducts(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/products", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task InvalidFilterValues_DoNotCrash()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products?sort=gibberish&page=-3&page-size=9999&min-price=900&max-price=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProductListing_ContainsSeededProducts()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/products?sort=newest");

        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("Cashmere Crew Neck Sweater", content);
        Assert.Contains("Trail Running Shoe", content);
    }
}
