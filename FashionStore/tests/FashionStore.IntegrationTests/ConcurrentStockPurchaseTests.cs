using System.Net;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Verifies the last-unit stock race at checkout: when two guest shoppers place an
/// order for the final unit of the same variant at the same time, exactly one
/// order wins and the other is rejected with a stock error. The in-transaction
/// re-verification of stock happens before either reservation commits, so the
/// second placement observes the first reservation and fails fast.
/// </summary>
public class ConcurrentStockPurchaseTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConcurrentStockPurchaseTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static bool? GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetBoolean() : null;

    private async Task ResetSweaterStockAsync(int onHand)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "SW-1001-GREY-M");
        variant.StockQuantity = onHand;
        variant.ReservedStock = 0;

        var stocks = await db.WarehouseStocks.Where(s => s.ProductVariantId == variant.Id).ToListAsync();
        foreach (var stock in stocks)
        {
            stock.OnHandQuantity = onHand;
            stock.ReservedQuantity = 0;
        }

        await db.SaveChangesAsync();
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
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    private async Task<string> CalculateAsync(HttpClient client, string email)
    {
        var methodId = await GetStandardShippingMethodIdAsync();
        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);

        var payload = JsonSerializer.Serialize(new
        {
            guestEmail = email,
            guestPhone = "555-0100",
            shippingAddress = BuildAddress(),
            billingAddress = (object?)null,
            billingSameAsShipping = true,
            shippingMethodId = methodId,
            paymentMethodCode = "card",
            termsAccepted = true,
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
        Assert.True(GetBoolean(result, "isValid"));
        return GetString(result, "continuationToken") ?? string.Empty;
    }

    private async Task<JsonElement> PlaceOrderAsync(HttpClient client, string email, string idempotencyKey, string continuationToken)
    {
        var methodId = await GetStandardShippingMethodIdAsync();
        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);

        var payload = JsonSerializer.Serialize(new
        {
            guestEmail = email,
            guestPhone = "555-0100",
            shippingAddress = BuildAddress(),
            billingAddress = (object?)null,
            billingSameAsShipping = true,
            shippingMethodId = methodId,
            paymentMethodCode = "card",
            termsAccepted = true,
            continuationToken,
            idempotencyKey
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/checkout/place")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<Guid> GetStandardShippingMethodIdAsync()
    {
        var client = _factory.CreateClient();
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

    private static object BuildAddress() => new
    {
        savedAddressId = (string?)null,
        recipientName = "Jane Doe",
        phone = "555-0100",
        addressLine1 = "1 Main Street",
        addressLine2 = (string?)null,
        area = (string?)null,
        city = "New York",
        region = "NY",
        postalCode = "10001",
        countryCode = "US",
        deliveryInstructions = (string?)null
    };

    private static string UniqueEmail() => $"race-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task TwoShoppers_RacingForLastUnit_ExactlyOneOrderWins()
    {
        await ResetSweaterStockAsync(1);

        var clientA = CreateClient();
        var clientB = CreateClient();
        var emailA = UniqueEmail();
        var emailB = UniqueEmail();

        await AddSweaterToCartAsync(clientA);
        await AddSweaterToCartAsync(clientB);

        var tokenA = await CalculateAsync(clientA, emailA);
        var tokenB = await CalculateAsync(clientB, emailB);

        var results = await Task.WhenAll(
            PlaceOrderAsync(clientA, emailA, $"race-a-{Guid.NewGuid():N}", tokenA),
            PlaceOrderAsync(clientB, emailB, $"race-b-{Guid.NewGuid():N}", tokenB));

        var successes = results.Where(r => GetBoolean(r, "success") == true).ToList();
        var failures = results.Where(r => GetBoolean(r, "success") == false).ToList();

        // Exactly one shopper secures the final unit; the other is told the item
        // is no longer available in the requested quantity.
        Assert.Single(successes);
        Assert.Single(failures);
        var error = Assert.Single(failures[0].GetProperty("errors").EnumerateArray());
        var code = GetString(error, "code");
        Assert.True(code is "stock" or "unavailable-item", $"Expected a stock error but got '{code}'");

        // The losing order must not exist in the database.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var winnerNumber = GetString(successes[0], "orderNumber");
        var loserNumber = GetString(failures[0], "orderNumber");
        Assert.NotNull(winnerNumber);
        Assert.Null(loserNumber);

        // The variant's reserved total matches exactly one unit: no oversell.
        var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "SW-1001-GREY-M");
        Assert.Equal(1, variant.ReservedStock);

        var orderCount = await db.Orders.CountAsync(o => o.PublicOrderNumber == winnerNumber);
        Assert.Equal(1, orderCount);

        var activeReservations = await db.StockReservations.CountAsync(r =>
            r.Status == StockReservationStatus.Active &&
            (r.CartReference == winnerNumber || r.ReferenceId == winnerNumber));
        Assert.Equal(1, activeReservations);
    }
}
