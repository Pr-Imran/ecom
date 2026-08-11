using System.Net;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Exercises the customer order panel: the signed-in order list (ownership,
/// search and status filter), secure guest lookup (order number + email), order
/// detail with timeline, guest cancellation with stock/coupon release, and
/// buy-again availability resolved against the live catalogue.
/// </summary>
public class OrderPanelTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrderPanelTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static string UniqueOrderNumber() => $"ORD-IT-{Guid.NewGuid():N}"[..24];

    private static string UniqueEmail() => $"panel-{Guid.NewGuid():N}@example.com";

    // ---- Seeding helpers ----

    private async Task<Order> SeedOrderAsync(
        string number,
        string? userId,
        string? guestEmail,
        OrderStatus status = OrderStatus.Placed,
        PaymentStatus payment = PaymentStatus.Unpaid,
        bool withItem = true,
        Guid? variantId = null,
        bool withAddress = true,
        bool withHistory = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = new Order
        {
            PublicOrderNumber = number,
            UserId = userId,
            GuestEmail = guestEmail,
            GuestPhone = "555-0100",
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = 128m,
            ProductDiscount = 0m,
            CouponDiscount = 0m,
            ShippingCharge = 9.99m,
            Tax = 0m,
            GrandTotal = 137.99m,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard Delivery",
            OrderStatus = status,
            PaymentStatus = payment,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (withItem)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = CartTestsHelper.GetProductId(_factory, "cashmere-crew-neck-sweater"),
                ProductVariantId = variantId ?? CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M"),
                ProductName = "Cashmere Crew Neck Sweater",
                ProductSlug = "cashmere-crew-neck-sweater",
                Sku = "SW-1001-GREY-M",
                ColourName = "Heather Grey",
                SizeName = "M",
                ImageUrl = "/img/sweater.jpg",
                UnitPrice = 128m,
                CompareAtPrice = 160m,
                Discount = 0m,
                Tax = 0m,
                Quantity = 1,
                LineTotal = 128m
            });
        }

        if (withAddress)
        {
            order.ShippingAddress = new OrderAddress
            {
                AddressType = OrderAddressType.Shipping,
                RecipientName = "Jane Doe",
                Phone = "555-0100",
                AddressLine1 = "1 Main Street",
                City = "New York",
                Region = "NY",
                PostalCode = "10001",
                CountryCode = "US"
            };
        }

        if (withHistory)
        {
            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = null,
                ToStatus = OrderStatus.Placed,
                Note = "Order placed",
                CreatedBy = "Jane Doe",
                CreatedAtUtc = order.CreatedAtUtc
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task<StockReservation> SeedReservationAsync(string number, Guid variantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reservation = new StockReservation
        {
            ProductVariantId = variantId,
            Quantity = 1,
            CartReference = number,
            ReferenceId = number,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            Status = StockReservationStatus.Active
        };
        db.StockReservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation;
    }

    private async Task<CouponUsage> SeedCouponUsageAsync(string number)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.Coupons.AnyAsync(c => c.Code == "PANEL20"))
        {
            db.Coupons.Add(new Coupon
            {
                Code = "PANEL20",
                NormalizedCode = "PANEL20",
                Name = "Panel 20%",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 20m,
                IsActive = true,
                PerCustomerLimit = 5,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var coupon = await db.Coupons.SingleAsync(c => c.Code == "PANEL20");
        var usage = new CouponUsage
        {
            CouponId = coupon.Id,
            UserId = "guest",
            OrderId = number,
            AmountDiscounted = 10m,
            UsedAtUtc = DateTime.UtcNow
        };
        db.CouponUsages.Add(usage);
        await db.SaveChangesAsync();
        return usage;
    }

    private async Task<(string Email, string UserId)> CreateConfirmedUserAsync()
    {
        var email = UniqueEmail();
        const string password = "PanelTest!pass1";
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return (email, user.Id);
    }

    private async Task<HttpClient> LoggedInClientAsync(string email, string password)
    {
        var client = CreateClient();
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var loginToken = CartTestsHelper.ExtractAntiforgeryToken(loginHtml);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = loginToken,
                ["EmailOrUserName"] = email,
                ["Password"] = password
            })
        };
        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        return client;
    }

    // ---- Signed-in order list ----

    [Fact]
    public async Task Orders_SignedIn_List_ShowsOnlyOwnOrders()
    {
        var (email, userId) = await CreateConfirmedUserAsync();
        var ownNumber = UniqueOrderNumber();
        var otherNumber = UniqueOrderNumber();
        await SeedOrderAsync(ownNumber, userId, email);
        await SeedOrderAsync(otherNumber, null, UniqueEmail());

        var client = await LoggedInClientAsync(email, "PanelTest!pass1");
        var html = await client.GetStringAsync("/orders");

        Assert.Contains(ownNumber, html);
        Assert.DoesNotContain(otherNumber, html);
        Assert.Contains("My Orders", html);
    }

    [Fact]
    public async Task Orders_SignedIn_List_SearchMatchesProductAndStatusFilter()
    {
        var (email, userId) = await CreateConfirmedUserAsync();
        var placedNumber = UniqueOrderNumber();
        var cancelledNumber = UniqueOrderNumber();
        await SeedOrderAsync(placedNumber, userId, email, status: OrderStatus.Placed);
        await SeedOrderAsync(cancelledNumber, userId, email, status: OrderStatus.Cancelled);

        var client = await LoggedInClientAsync(email, "PanelTest!pass1");

        var byNumber = await client.GetStringAsync($"/orders?search={cancelledNumber}");
        Assert.Contains(cancelledNumber, byNumber);
        Assert.DoesNotContain(placedNumber, byNumber);

        var byStatus = await client.GetStringAsync("/orders?status=Cancelled");
        Assert.Contains(cancelledNumber, byStatus);
        Assert.DoesNotContain(placedNumber, byStatus);

        var byProduct = await client.GetStringAsync("/orders?search=cashmere");
        Assert.Contains(placedNumber, byProduct);
        Assert.Contains(cancelledNumber, byProduct);
    }

    [Fact]
    public async Task Orders_SignedIn_List_EmptyStateRenders()
    {
        var (email, _) = await CreateConfirmedUserAsync();
        var client = await LoggedInClientAsync(email, "PanelTest!pass1");

        var html = await client.GetStringAsync("/orders");

        Assert.Contains("No orders yet", html);
    }

    [Fact]
    public async Task Orders_SignedIn_Detail_RendersItemsTimelineAndAddresses()
    {
        var (email, userId) = await CreateConfirmedUserAsync();
        var number = UniqueOrderNumber();
        await SeedOrderAsync(number, userId, email);

        var client = await LoggedInClientAsync(email, "PanelTest!pass1");
        var html = await client.GetStringAsync($"/orders/{number}");

        Assert.Contains(number, html);
        Assert.Contains("Cashmere Crew Neck Sweater", html);
        Assert.Contains("Heather Grey", html);
        Assert.Contains("Order timeline", html);
        Assert.Contains("1 Main Street", html);
        Assert.Contains("Payment summary", html);
        Assert.Contains("Buy again", html);
    }

    [Fact]
    public async Task Orders_SignedIn_Detail_DeliveredOrder_ShowsReturnRequestPlaceholder()
    {
        var (email, userId) = await CreateConfirmedUserAsync();
        var number = UniqueOrderNumber();
        await SeedOrderAsync(
            number,
            userId,
            email,
            status: OrderStatus.Delivered,
            payment: PaymentStatus.Paid);

        var client = await LoggedInClientAsync(email, "PanelTest!pass1");
        var html = await client.GetStringAsync($"/orders/{number}");

        Assert.Contains("Request a return", html);
        Assert.Contains("data-return-button", html);
    }

    [Fact]
    public async Task Orders_SignedIn_Detail_AnotherUsersOrder_ReturnsNotFound()
    {
        var (email, userId) = await CreateConfirmedUserAsync();
        var number = UniqueOrderNumber();
        await SeedOrderAsync(number, userId, email);

        var (otherEmail, _) = await CreateConfirmedUserAsync();
        var client = await LoggedInClientAsync(otherEmail, "PanelTest!pass1");

        var response = await client.GetAsync($"/orders/{number}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Guest lookup ----

    [Fact]
    public async Task Orders_GuestLookup_ValidEmail_IssuesTokenAndRendersDetail()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        await SeedOrderAsync(number, null, email);

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var token = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);

        var post = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["publicOrderNumber"] = number,
                ["email"] = email
            })
        };
        var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains($"/orders/{number}", location);
        Assert.Contains("t=", location);

        var detailHtml = await client.GetStringAsync(location);
        Assert.Contains(number, detailHtml);
        Assert.Contains("Cashmere Crew Neck Sweater", detailHtml);
        Assert.Contains("Order timeline", detailHtml);
    }

    [Fact]
    public async Task Orders_GuestLookup_WrongEmail_FailsAmbiguously()
    {
        var number = UniqueOrderNumber();
        await SeedOrderAsync(number, null, UniqueEmail());

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var token = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);

        var post = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["publicOrderNumber"] = number,
                ["email"] = "wrong@example.com"
            })
        };
        var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("could not find an order", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orders_GuestLookup_UnknownOrder_FailsAmbiguously()
    {
        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var token = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);

        var post = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["publicOrderNumber"] = "ORD-9999-999999",
                ["email"] = "guest@example.com"
            })
        };
        var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("could not find an order", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orders_GuestDetail_WithoutToken_RedirectsToTrack()
    {
        var number = UniqueOrderNumber();
        await SeedOrderAsync(number, null, UniqueEmail());

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/{number}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/orders/track", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Orders_GuestDetail_InvalidToken_RedirectsToTrack()
    {
        var number = UniqueOrderNumber();
        await SeedOrderAsync(number, null, UniqueEmail());

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/{number}?t=not-a-valid-token");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/orders/track", response.Headers.Location?.ToString());
    }

    // ---- Cancellation ----

    [Fact]
    public async Task Orders_Cancel_GuestOrder_CancelsAndReleasesStockAndVoidsCoupon()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");
        await SeedOrderAsync(number, null, email, variantId: variantId);
        var reservation = await SeedReservationAsync(number, variantId);
        var usage = await SeedCouponUsageAsync(number);

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var trackToken = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);

        // Obtain a valid guest ticket.
        var lookup = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = trackToken,
                ["publicOrderNumber"] = number,
                ["email"] = email
            })
        };
        var lookupResponse = await client.SendAsync(lookup);
        Assert.Equal(HttpStatusCode.Redirect, lookupResponse.StatusCode);
        var location = lookupResponse.Headers.Location!.ToString();
        var guestToken = GetQueryValue(location, "t");
        Assert.False(string.IsNullOrEmpty(guestToken));

        // Cancel with the guest ticket.
        var cancelHtml = await client.GetStringAsync(location);
        var cancelToken = CartTestsHelper.ExtractAntiforgeryToken(cancelHtml);
        var cancel = new HttpRequestMessage(HttpMethod.Post, $"/orders/{number}/cancel")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = cancelToken,
                ["reason"] = "ChangedMind",
                ["t"] = guestToken
            })
        };
        var cancelResponse = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Redirect, cancelResponse.StatusCode);
        Assert.Contains($"/orders/{number}", cancelResponse.Headers.Location?.ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = await db.Orders
            .Include(o => o.StatusHistory)
            .SingleAsync(o => o.PublicOrderNumber == number);
        Assert.Equal(OrderStatus.Cancelled, order.OrderStatus);
        Assert.NotNull(order.CancelledAtUtc);
        Assert.Equal(OrderCancellationReason.ChangedMind.ToString(), order.CancelledReasonCode);
        Assert.Equal(2, order.StatusHistory.Count);
        Assert.Contains(order.StatusHistory, h => h.ToStatus == OrderStatus.Cancelled);

        var refreshedReservation = await db.StockReservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(StockReservationStatus.Released, refreshedReservation.Status);

        var refreshedUsage = await db.CouponUsages.SingleAsync(u => u.Id == usage.Id);
        Assert.NotNull(refreshedUsage.VoidedAtUtc);
    }

    [Fact]
    public async Task Orders_Cancel_PaidOrder_RefusesCancellation()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        await SeedOrderAsync(number, null, email, payment: PaymentStatus.Paid);

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var trackToken = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);
        var lookup = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = trackToken,
                ["publicOrderNumber"] = number,
                ["email"] = email
            })
        };
        var lookupResponse = await client.SendAsync(lookup);
        var location = lookupResponse.Headers.Location!.ToString();
        var guestToken = GetQueryValue(location, "t");
        Assert.False(string.IsNullOrEmpty(guestToken));

        var cancelHtml = await client.GetStringAsync(location);
        var cancelToken = CartTestsHelper.ExtractAntiforgeryToken(cancelHtml);
        var cancel = new HttpRequestMessage(HttpMethod.Post, $"/orders/{number}/cancel")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = cancelToken,
                ["reason"] = "ChangedMind",
                ["t"] = guestToken
            })
        };
        var cancelResponse = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Redirect, cancelResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.PublicOrderNumber == number);
        Assert.Equal(OrderStatus.Placed, order.OrderStatus);
        Assert.Null(order.CancelledAtUtc);
    }

    [Fact]
    public async Task Orders_Cancel_ProgressedOrder_RefusesCancellation()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        await SeedOrderAsync(number, null, email, status: OrderStatus.Shipped, payment: PaymentStatus.Paid);

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var trackToken = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);
        var lookup = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = trackToken,
                ["publicOrderNumber"] = number,
                ["email"] = email
            })
        };
        var lookupResponse = await client.SendAsync(lookup);
        var location = lookupResponse.Headers.Location!.ToString();
        var guestToken = GetQueryValue(location, "t");
        Assert.False(string.IsNullOrEmpty(guestToken));

        var cancelHtml = await client.GetStringAsync(location);
        var cancelToken = CartTestsHelper.ExtractAntiforgeryToken(cancelHtml);
        var cancel = new HttpRequestMessage(HttpMethod.Post, $"/orders/{number}/cancel")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = cancelToken,
                ["reason"] = "ChangedMind",
                ["t"] = guestToken
            })
        };
        var cancelResponse = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.Redirect, cancelResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.PublicOrderNumber == number);
        Assert.Equal(OrderStatus.Shipped, order.OrderStatus);
    }

    // ---- Buy again ----

    [Fact]
    public async Task Orders_BuyAgain_AvailableVariant_ReturnsAvailable()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");
        await SeedOrderAsync(number, null, email, variantId: variantId);

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var token = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);
        var lookup = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["publicOrderNumber"] = number,
                ["email"] = email
            })
        };
        var lookupResponse = await client.SendAsync(lookup);
        var guestToken = GetQueryValue(lookupResponse.Headers.Location!.ToString(), "t");
        Assert.False(string.IsNullOrEmpty(guestToken));

        var detailHtml = await client.GetStringAsync($"/orders/{number}?t={guestToken}");
        var cancelToken = CartTestsHelper.ExtractAntiforgeryToken(detailHtml);
        var post = new HttpRequestMessage(HttpMethod.Post, $"/orders/{number}/buy-again")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = cancelToken,
                ["t"] = guestToken
            })
        };
        var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("items");
        var first = Assert.Single(items.EnumerateArray());
        Assert.True(first.GetProperty("isAvailable").GetBoolean());
        Assert.Equal("SW-1001-GREY-M", GetString(first, "sku"));
    }

    [Fact]
    public async Task Orders_BuyAgain_MissingVariant_MarksUnavailable()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        // Order line references a variant that does not exist in the catalogue.
        await SeedOrderAsync(number, null, email, variantId: Guid.NewGuid());

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var token = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);
        var lookup = new HttpRequestMessage(HttpMethod.Post, "/orders/track")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["publicOrderNumber"] = number,
                ["email"] = email
            })
        };
        var lookupResponse = await client.SendAsync(lookup);
        var guestToken = GetQueryValue(lookupResponse.Headers.Location!.ToString(), "t");
        Assert.False(string.IsNullOrEmpty(guestToken));

        var detailHtml = await client.GetStringAsync($"/orders/{number}?t={guestToken}");
        var cancelToken = CartTestsHelper.ExtractAntiforgeryToken(detailHtml);
        var post = new HttpRequestMessage(HttpMethod.Post, $"/orders/{number}/buy-again")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = cancelToken,
                ["t"] = guestToken
            })
        };
        var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var first = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.False(first.GetProperty("isAvailable").GetBoolean());
        Assert.False(string.IsNullOrEmpty(GetString(first, "unavailableReason")));
    }

    [Fact]
    public async Task Orders_BuyAgain_WithoutTicket_ReturnsUnauthorized()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        await SeedOrderAsync(number, null, email);

        var client = CreateClient();
        var trackHtml = await client.GetStringAsync("/orders/track");
        var token = CartTestsHelper.ExtractAntiforgeryToken(trackHtml);
        var post = new HttpRequestMessage(HttpMethod.Post, $"/orders/{number}/buy-again")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            })
        };
        var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string? GetQueryValue(string url, string key)
    {
        var query = url.Contains('?') ? url[(url.IndexOf('?') + 1)..] : string.Empty;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }
}
