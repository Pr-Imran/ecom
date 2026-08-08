using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FashionStore.IntegrationTests;

public class CheckoutTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CheckoutTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static decimal? GetDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetDecimal() : null;

    private static bool? GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetBoolean() : null;

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

    private static async Task<(JsonElement Result, string Token)> CalculateAsync(
        HttpClient client,
        Guid? shippingMethodId,
        string paymentMethod = "card",
        string? email = "guest@example.com",
        string? phone = "555-0100",
        bool terms = true)
    {
        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);

        var shippingAddress = new
        {
            savedAddressId = (string?)null,
            recipientName = "Jane Doe",
            phone,
            addressLine1 = "1 Main Street",
            addressLine2 = (string?)null,
            area = (string?)null,
            city = "New York",
            region = "NY",
            postalCode = "10001",
            countryCode = "US",
            deliveryInstructions = (string?)null
        };

        var payload = JsonSerializer.Serialize(new
        {
            guestEmail = email,
            guestPhone = phone,
            shippingAddress,
            billingAddress = (object?)null,
            billingSameAsShipping = true,
            shippingMethodId,
            paymentMethodCode = paymentMethod,
            termsAccepted = terms,
            continuationToken = (string?)null
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/checkout/calculate")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var result = doc.RootElement.Clone();
        return (result, GetString(result, "continuationToken") ?? string.Empty);
    }

    private static async Task<Guid> GetStandardShippingMethodIdAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
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
        var standard = doc.RootElement.GetProperty("quotes")
            .EnumerateArray()
            .Single(q => q.GetProperty("code").GetString() == "STANDARD");
        return standard.GetProperty("methodId").GetGuid();
    }

    [Fact]
    public async Task Checkout_Calculate_ValidCart_ComputesServerPricedTotals()
    {
        var client = CreateClient();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (result, _) = await CalculateAsync(client, methodId);

        Assert.True(GetBoolean(result, "isValid"));
        Assert.Equal(0, result.GetProperty("errors").GetArrayLength());

        var totals = result.GetProperty("totals");
        Assert.Equal(128.00m, GetDecimal(totals, "subtotal"));
        Assert.Equal(9.99m, GetDecimal(totals, "shipping"));
        Assert.Equal(137.99m, GetDecimal(totals, "grandTotal"));
        Assert.Equal("USD", GetString(totals, "currency"));

        Assert.Single(result.GetProperty("lines").EnumerateArray());
        Assert.Equal(2, result.GetProperty("shippingOptions").GetArrayLength());
        Assert.NotNull(GetString(result, "continuationToken"));
    }

    [Fact]
    public async Task Checkout_Calculate_MissingTerms_ReportsValidationError()
    {
        var client = CreateClient();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (result, _) = await CalculateAsync(client, methodId, terms: false);

        Assert.False(GetBoolean(result, "isValid"));
        var errors = result.GetProperty("errors");
        Assert.Contains(errors.EnumerateArray(), e =>
            GetString(e, "field") == "terms" && GetString(e, "code") == "not-accepted");
    }

    [Fact]
    public async Task Checkout_Calculate_CodOnStandardDelivery_IsValid()
    {
        var client = CreateClient();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (result, _) = await CalculateAsync(client, methodId, paymentMethod: "cod");

        Assert.True(GetBoolean(result, "isValid"));
        var selected = result.GetProperty("selectedShipping");
        Assert.True(GetBoolean(selected, "supportsCashOnDelivery"));
    }

    [Fact]
    public async Task Checkout_Calculate_StableToken_DetectsNoPriceChange()
    {
        var client = CreateClient();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (first, token) = await CalculateAsync(client, methodId);
        Assert.True(GetBoolean(first, "isValid"));
        Assert.False(string.IsNullOrEmpty(token));

        var (second, token2) = await CalculateAsync(client, methodId);
        Assert.False(GetBoolean(second, "pricesChanged"));
        Assert.Equal(token, token2);
    }

    [Fact]
    public async Task Checkout_Calculate_EmptyCart_ReportsCartError()
    {
        var client = CreateClient();

        var (result, _) = await CalculateAsync(client, null);

        Assert.False(GetBoolean(result, "isValid"));
        var errors = result.GetProperty("errors");
        Assert.Contains(errors.EnumerateArray(), e =>
            GetString(e, "field") == "cart" && GetString(e, "code") == "empty");
    }
}
