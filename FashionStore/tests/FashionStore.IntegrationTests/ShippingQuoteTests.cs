using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FashionStore.IntegrationTests;

public class ShippingQuoteTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ShippingQuoteTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private async Task AddSweaterToCartAsync(HttpClient client)
    {
        var productId = CartTestsHelper.GetProductId(_factory, "cashmere-crew-neck-sweater");
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 1 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ShippingQuote_AnonymousCart_ReturnsServerPricedMethods()
    {
        var client = CreateClient();
        await AddSweaterToCartAsync(client);

        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { countryCode = "US", city = "New York", region = "NY", postalCode = "10001" });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/shipping/quote")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("isSupported").GetBoolean());

        var quotes = doc.RootElement.GetProperty("quotes").EnumerateArray().ToList();
        Assert.Equal(2, quotes.Count);

        var standard = quotes.Single(q => q.GetProperty("code").GetString() == "STANDARD");
        Assert.True(standard.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(9.99m, standard.GetProperty("price").GetDecimal());
        Assert.True(standard.GetProperty("supportsCashOnDelivery").GetBoolean());
        Assert.Equal(3, standard.GetProperty("estimatedMinDays").GetInt32());

        var express = quotes.Single(q => q.GetProperty("code").GetString() == "EXPRESS");
        Assert.True(express.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(24.99m, express.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task ShippingQuote_UnsupportedCountry_ReturnsNotSupported()
    {
        var client = CreateClient();
        await AddSweaterToCartAsync(client);

        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { countryCode = "XX" });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/shipping/quote")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("isSupported").GetBoolean());
        Assert.NotNull(doc.RootElement.GetProperty("unsupportedReason").GetString());
    }

    [Fact]
    public async Task ShippingQuote_MissingCountry_ReturnsBadRequest()
    {
        var client = CreateClient();

        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/shipping/quote")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
