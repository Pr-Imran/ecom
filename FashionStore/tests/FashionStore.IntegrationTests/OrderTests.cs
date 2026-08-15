using System.Net;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

public class OrderTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrderTests(TestWebApplicationFactory factory)
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

    private async Task AddSweaterToCartAsync(HttpClient client, int quantity = 1)
    {
        var productId = CartTestsHelper.GetProductId(_factory, "cashmere-crew-neck-sweater");
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object BuildAddress(string recipientName = "Jane Doe") => new
    {
        savedAddressId = (string?)null,
        recipientName,
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

    private static async Task<(JsonElement Result, string Token)> CalculateAsync(
        HttpClient client,
        Guid? shippingMethodId,
        string paymentMethod = "card",
        string? email = "guest@example.com")
    {
        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);

        var payload = JsonSerializer.Serialize(new
        {
            guestEmail = email,
            guestPhone = "555-0100",
            shippingAddress = BuildAddress(),
            billingAddress = (object?)null,
            billingSameAsShipping = true,
            shippingMethodId,
            paymentMethodCode = paymentMethod,
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
        return (result, GetString(result, "continuationToken") ?? string.Empty);
    }

    private static async Task<JsonElement> PlaceOrderAsync(
        HttpClient client,
        Guid? shippingMethodId,
        string idempotencyKey,
        string paymentMethod = "card",
        string? email = "guest@example.com",
        string? continuationToken = null)
    {
        var pageHtml = await client.GetStringAsync("/cart");
        var token = CartTestsHelper.ExtractAntiforgeryToken(pageHtml);

        var payload = JsonSerializer.Serialize(new
        {
            guestEmail = email,
            guestPhone = "555-0100",
            shippingAddress = BuildAddress(),
            billingAddress = (object?)null,
            billingSameAsShipping = true,
            shippingMethodId,
            paymentMethodCode = paymentMethod,
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

    /// <summary>
    /// The in-memory database is shared across every test in the factory, so each
    /// order test resets the sweater's stock to a known baseline and uses a unique
    /// guest email to stay independent of earlier runs.
    /// </summary>
    private async Task ResetSweaterStockAsync(int onHand = 10)
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

    private static string UniqueEmail() => $"order-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Place_ValidGuestCardOrder_CreatesOrderWithSnapshotsAndReservesStock()
    {
        await ResetSweaterStockAsync();
        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (calculation, _) = await CalculateAsync(client, methodId, email: email);

        Assert.True(GetBoolean(calculation, "isValid"));

        var idempotencyKey = $"order-{Guid.NewGuid():N}";
        var result = await PlaceOrderAsync(client, methodId, idempotencyKey, email: email);

        Assert.True(GetBoolean(result, "success"));
        Assert.False(GetBoolean(result, "isDuplicate"));
        var orderNumber = GetString(result, "orderNumber");
        Assert.NotNull(orderNumber);
        Assert.StartsWith("ORD-", orderNumber);
        Assert.Equal(137.99m, GetDecimal(result, "grandTotal"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.ShippingAddress)
            .Include(o => o.StatusHistory)
            .SingleAsync(o => o.PublicOrderNumber == orderNumber);

        Assert.Equal(OrderStatus.Placed, order.OrderStatus);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Unfulfilled, order.FulfilmentStatus);
        Assert.Equal("card", order.PaymentMethodCode);
        Assert.Equal(email, order.GuestEmail);
        Assert.Equal(128.00m, order.Subtotal);
        Assert.Equal(9.99m, order.ShippingCharge);
        Assert.Equal(137.99m, order.GrandTotal);

        var item = Assert.Single(order.Items);
        Assert.Equal("Cashmere Crew Neck Sweater", item.ProductName);
        Assert.Equal("SW-1001-GREY-M", item.Sku);
        Assert.Equal(128.00m, item.UnitPrice);
        Assert.Equal(1, item.Quantity);

        Assert.NotNull(order.ShippingAddress);
        Assert.Equal("Jane Doe", order.ShippingAddress.RecipientName);
        Assert.Equal("10001", order.ShippingAddress.PostalCode);

        var history = Assert.Single(order.StatusHistory);
        Assert.Equal(OrderStatus.Placed, history.ToStatus);

        var reservation = await db.StockReservations.SingleAsync(r => r.ReferenceId == orderNumber);
        Assert.Equal(StockReservationStatus.Active, reservation.Status);

        var tx = await db.InventoryTransactions.SingleAsync(t => t.ReferenceId == orderNumber);
        Assert.Equal(InventoryReferenceType.Order, tx.ReferenceType);
        Assert.Equal(StockAdjustmentReason.OrderReservation, tx.Reason);

        var idempotency = await db.OrderIdempotencyRecords.SingleAsync(r => r.IdempotencyKey == idempotencyKey);
        Assert.Equal(order.Id, idempotency.OrderId);
    }

    [Fact]
    public async Task Place_SameIdempotencyKeyTwice_ReturnsExistingOrderOnly()
    {
        await ResetSweaterStockAsync();
        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (calculation, _) = await CalculateAsync(client, methodId, email: email);
        Assert.True(GetBoolean(calculation, "isValid"));

        var idempotencyKey = $"dup-{Guid.NewGuid():N}";
        var first = await PlaceOrderAsync(client, methodId, idempotencyKey, email: email);
        var second = await PlaceOrderAsync(client, methodId, idempotencyKey, email: email);

        Assert.True(GetBoolean(first, "success"));
        Assert.True(GetBoolean(second, "success"));
        Assert.True(GetBoolean(second, "isDuplicate"));
        Assert.Equal(GetString(first, "orderNumber"), GetString(second, "orderNumber"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderCount = await db.Orders.CountAsync(o => o.PublicOrderNumber == GetString(first, "orderNumber"));
        Assert.Equal(1, orderCount);
    }

    [Fact]
    public async Task Place_StockShortage_ReturnsStockErrorAndNoOrder()
    {
        await ResetSweaterStockAsync(1);
        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (calculation, token) = await CalculateAsync(client, methodId, email: email);
        Assert.True(GetBoolean(calculation, "isValid"));

        // Drain the remaining unit so the placement-time stock verification fails.
        using (var drainScope = _factory.Services.CreateScope())
        {
            var db = drainScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "SW-1001-GREY-M");
            variant.StockQuantity = 0;
            variant.ReservedStock = 0;
            await db.SaveChangesAsync();
        }

        var result = await PlaceOrderAsync(client, methodId, $"stock-{Guid.NewGuid():N}", email: email, continuationToken: token);

        Assert.False(GetBoolean(result, "success"));
        var errors = result.GetProperty("errors");
        // The shortage is reported either by the engine (item unavailable when the
        // cart is re-resolved) or by the placement-time stock verification. Both
        // must leave the order uncreated.
        Assert.Contains(errors.EnumerateArray(), e =>
            GetString(e, "code") == "stock" || GetString(e, "code") == "unavailable-item");

        using var scope = _factory.Services.CreateScope();
        var check = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await check.Orders.AnyAsync(o => o.GuestEmail == email));
    }

    [Fact]
    public async Task Place_StaleContinuationToken_RefusesPlacement()
    {
        await ResetSweaterStockAsync();
        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (calculation, _) = await CalculateAsync(client, methodId, email: email);
        Assert.True(GetBoolean(calculation, "isValid"));

        // Place with a different guest email while echoing the original token: the
        // engine recomputes a fresh token, detects the mismatch and refuses.
        var result = await PlaceOrderAsync(
            client,
            methodId,
            $"stale-{Guid.NewGuid():N}",
            email: email + ".different",
            continuationToken: GetString(calculation, "continuationToken"));

        Assert.False(GetBoolean(result, "success"));
        var errors = result.GetProperty("errors");
        Assert.Contains(errors.EnumerateArray(), e =>
            GetString(e, "code") == "prices-changed");
    }

    [Fact]
    public async Task Place_EmptyCart_ReportsCartError()
    {
        var client = CreateClient();

        var result = await PlaceOrderAsync(client, null, $"empty-{Guid.NewGuid():N}");

        Assert.False(GetBoolean(result, "success"));
        var errors = result.GetProperty("errors");
        Assert.Contains(errors.EnumerateArray(), e =>
            GetString(e, "field") == "cart" && GetString(e, "code") == "empty");
    }

    [Fact]
    public async Task Confirmation_Page_RendersStoredOrder()
    {
        await ResetSweaterStockAsync();
        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (calculation, _) = await CalculateAsync(client, methodId, email: email);
        Assert.True(GetBoolean(calculation, "isValid"));

        var result = await PlaceOrderAsync(client, methodId, $"confirm-{Guid.NewGuid():N}", email: email);
        var orderNumber = GetString(result, "orderNumber");
        Assert.NotNull(orderNumber);

        // Guest orders are ticket-gated: the confirmation screen requires the
        // signed access ticket issued at placement, never the order number alone.
        var page = await client.GetStringAsync($"/checkout/confirmation/{orderNumber}?t={GetString(result, "guestAccessToken")}");
        Assert.Contains(orderNumber, page);
        Assert.Contains("Cashmere Crew Neck Sweater", page);
        Assert.Contains("Jane Doe", page);
    }

    [Fact]
    public async Task Confirmation_GuestOrder_WithoutTicket_ReturnsNotFound()
    {
        await ResetSweaterStockAsync();
        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (calculation, _) = await CalculateAsync(client, methodId, email: email);
        Assert.True(GetBoolean(calculation, "isValid"));

        var result = await PlaceOrderAsync(client, methodId, $"confirm-noticket-{Guid.NewGuid():N}", email: email);
        var orderNumber = GetString(result, "orderNumber");
        Assert.NotNull(orderNumber);

        // An anonymous caller with only the order number must not see the order.
        var response = await client.GetAsync($"/checkout/confirmation/{orderNumber}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Confirmation_GuestOrder_WithWrongTicket_ReturnsNotFound()
    {
        await ResetSweaterStockAsync();
        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);
        var (calculation, _) = await CalculateAsync(client, methodId, email: email);
        Assert.True(GetBoolean(calculation, "isValid"));

        var result = await PlaceOrderAsync(client, methodId, $"confirm-wrong-{Guid.NewGuid():N}", email: email);
        var orderNumber = GetString(result, "orderNumber");
        Assert.NotNull(orderNumber);

        // A ticket bound to a different order number must not authorize access.
        var other = await PlaceOrderAsync(client, methodId, $"confirm-other-{Guid.NewGuid():N}", email: UniqueEmail());
        var wrongTicket = GetString(other, "guestAccessToken");
        var response = await client.GetAsync($"/checkout/confirmation/{orderNumber}?t={wrongTicket}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Confirmation_UnknownOrder_ReturnsNotFound()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/checkout/confirmation/ORD-9999-999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Place_WithCoupon_RecordsCouponUsageAgainstOrderNumber()
    {
        await ResetSweaterStockAsync();
        const string couponCode = "WELCOME20";

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Coupons.AnyAsync(c => c.Code == couponCode))
            {
                db.Coupons.Add(new FashionStore.Domain.Entities.Coupon
                {
                    Code = couponCode,
                    NormalizedCode = couponCode.ToUpperInvariant(),
                    Name = "Welcome 20%",
                    DiscountType = DiscountType.Percentage,
                    DiscountValue = 20m,
                    IsActive = true,
                    PerCustomerLimit = 5,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

        var client = CreateClient();
        var email = UniqueEmail();
        await AddSweaterToCartAsync(client);

        var methodId = await GetStandardShippingMethodIdAsync(_factory);

        // Apply the coupon through the storefront cart endpoint (anonymous cookie).
        var cartHtml = await client.GetStringAsync("/cart");
        var cartToken = CartTestsHelper.ExtractAntiforgeryToken(cartHtml);
        var couponRequest = new HttpRequestMessage(HttpMethod.Post, "/cart/coupon")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { code = couponCode }), Encoding.UTF8, "application/json")
        };
        couponRequest.Headers.Add("RequestVerificationToken", cartToken);
        var couponResponse = await client.SendAsync(couponRequest);
        Assert.Equal(HttpStatusCode.OK, couponResponse.StatusCode);

        var (calculation, _) = await CalculateAsync(client, methodId, email: email);
        Assert.True(GetBoolean(calculation, "isValid"));
        Assert.Equal(128.00m, GetDecimal(calculation.GetProperty("totals"), "subtotal"));
        Assert.True(GetDecimal(calculation.GetProperty("totals"), "couponDiscount") > 0m);

        var result = await PlaceOrderAsync(client, methodId, $"coupon-{Guid.NewGuid():N}", email: email);
        Assert.True(GetBoolean(result, "success"));
        var orderNumber = GetString(result, "orderNumber");
        Assert.NotNull(orderNumber);

        using var scope = _factory.Services.CreateScope();
        var usageDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var usage = await usageDb.CouponUsages.SingleAsync(u => u.OrderId == orderNumber);
        Assert.NotNull(usage);
        Assert.Equal(usageKey(email), usage.UserId);

        var coupon = await usageDb.Coupons.SingleAsync(c => c.Id == usage.CouponId);
        Assert.Equal(couponCode, coupon.Code);

        var order = await usageDb.Orders.SingleAsync(o => o.PublicOrderNumber == orderNumber);
        Assert.True(order.CouponDiscount > 0m);
    }

    private static string usageKey(string email) => email;
}
