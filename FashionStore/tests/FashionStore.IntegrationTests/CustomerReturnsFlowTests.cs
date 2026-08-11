using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Exercises the customer return panel end to end: the start/create wizard for
/// signed-in customers and guests (with the signed order ticket), the detail page,
/// photo uploads, marking the return as shipped back, withdrawing a return, and a
/// full store round-trip (create → admin approve → customer ship → admin
/// receive/inspect/refund → close) driven purely through the HTTP surfaces.
/// </summary>
public class CustomerReturnsFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "CustomerTest!pass1";

    private static readonly string[] AdminPermissions =
    {
        "Returns.View", "Returns.Review", "Returns.Inspect", "Returns.Restock",
        "Returns.Refund", "Returns.Exchange", "Returns.Complete"
    };

    private readonly WebApplicationFactory<Program> _factory;

    public CustomerReturnsFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"returncustomer-{Guid.NewGuid():N}@example.com";

    private static string UniqueOrderNumber() => $"ORD-C-{Guid.NewGuid():N}"[..24];

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

    // ---- Account helpers ----

    private async Task<(string Email, string UserId)> CreateCustomerAsync()
    {
        var email = UniqueEmail();
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
        var result = await userManager.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return (email, user.Id);
    }

    private async Task<HttpClient> CustomerClientAsync(string email)
    {
        var client = CreateClient();
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var token = CartTestsHelper.ExtractAntiforgeryToken(loginHtml);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = Password
            })
        };
        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        return client;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var email = UniqueEmail();
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
        var createResult = await userManager.CreateAsync(user, Password);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(e => e.Description)));

        foreach (var permission in AdminPermissions)
        {
            var claimResult = await userManager.AddClaimAsync(user, new Claim("permission", permission));
            Assert.True(claimResult.Succeeded, string.Join("; ", claimResult.Errors.Select(e => e.Description)));
        }

        return await CustomerClientAsync(email);
    }

    // ---- Order seeding ----

    private async Task<Order> SeedDeliveredOrderAsync(
        string number,
        string? userId = null,
        string? guestEmail = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var productId = CartTestsHelper.GetProductId(_factory, "cashmere-crew-neck-sweater");
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");
        var now = DateTime.UtcNow;

        var order = new Order
        {
            PublicOrderNumber = number,
            InvoiceNumber = number.Replace("ORD", "INV"),
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
            OrderStatus = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Paid,
            FulfilmentStatus = FulfilmentStatus.Fulfilled,
            DeliveredAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-7),
            UpdatedAtUtc = now
        };
        order.Items.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductId = productId,
            ProductVariantId = variantId,
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
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task<string> IssueGuestTokenAsync(string number, string email)
    {
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
        var response = await client.SendAsync(lookup);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        var guestToken = GetQueryValue(location!, "t");
        Assert.False(string.IsNullOrEmpty(guestToken));
        return guestToken!;
    }

    private static string ExtractLocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        return location!;
    }

    // ---- Signed-in customer flows ----

    [Fact]
    public async Task SignedInCustomer_CreatesReturn_ViaWizard()
    {
        var (email, userId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: userId);
        var client = await CustomerClientAsync(email);

        var startHtml = await client.GetStringAsync($"/returns/create/{order.PublicOrderNumber}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(startHtml);
        var orderItem = order.Items.First();

        var response = await client.PostAsync("/returns/create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["PublicOrderNumber"] = order.PublicOrderNumber,
            ["ReasonCode"] = "ChangedMind",
            ["Notes"] = "Wrong size, please help",
            ["Items[0].OrderItemId"] = orderItem.Id.ToString(),
            ["Items[0].Quantity"] = "1"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = ExtractLocationPath(response);
        Assert.Contains("/returns/", location);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ReturnRequests.Include(r => r.Items).Include(r => r.StatusHistory)
            .SingleAsync(r => r.OrderId == order.Id);
        Assert.Equal(ReturnStatus.Requested, stored.Status);
        Assert.Equal(ReturnReasonCode.ChangedMind, stored.ReasonCode);
        Assert.Equal(128m, stored.RefundableAmount);
        Assert.Single(stored.Items);
        Assert.Single(stored.StatusHistory);
    }

    [Fact]
    public async Task SignedInCustomer_ViewsOwnReturnDetail()
    {
        var (email, userId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: userId);
        var client = await CustomerClientAsync(email);

        var startHtml = await client.GetStringAsync($"/returns/create/{order.PublicOrderNumber}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(startHtml);
        var orderItem = order.Items.First();

        var create = await client.PostAsync("/returns/create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["PublicOrderNumber"] = order.PublicOrderNumber,
            ["ReasonCode"] = "WrongSize",
            ["Items[0].OrderItemId"] = orderItem.Id.ToString(),
            ["Items[0].Quantity"] = "1"
        }));
        var returnNumber = Path.GetFileName(ExtractLocationPath(create));

        var detail = await client.GetAsync($"/returns/{returnNumber}");

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains(returnNumber, html);
        Assert.Contains("WrongSize", html);
    }

    [Fact]
    public async Task SignedInCustomer_CannotStartReturnForAnotherUsersOrder()
    {
        var (_, ownerId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: ownerId);

        var (otherEmail, _) = await CreateCustomerAsync();
        var client = await CustomerClientAsync(otherEmail);

        var response = await client.GetAsync($"/returns/create/{order.PublicOrderNumber}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Cashmere Crew Neck Sweater", html);
    }

    [Fact]
    public async Task SignedInCustomer_ShippingMarksReturnInTransit()
    {
        var (email, userId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: userId);
        var client = await CustomerClientAsync(email);
        var admin = await AdminClientAsync();

        var returnNumber = await CreateReturnAsync(client, order);

        Guid returnId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var returnRequest = await seedDb.ReturnRequests.SingleAsync(r => r.ReturnNumber == returnNumber);
            returnId = returnRequest.Id;
            var approve = await admin.PostAsync($"/api/admin/returns/{returnId}/approve", Json(new { note = "OK" }));
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        }

        var detailHtml = await client.GetStringAsync($"/returns/{returnNumber}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(detailHtml);

        var ship = await client.PostAsync($"/returns/{returnNumber}/ship", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["carrierCode"] = "ups",
            ["trackingNumber"] = "1Z999AA10123456784"
        }));

        Assert.Equal(HttpStatusCode.Redirect, ship.StatusCode);
        Assert.Contains($"/returns/{returnNumber}", ExtractLocationPath(ship));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ReturnRequests.Include(r => r.StatusHistory).SingleAsync(r => r.ReturnNumber == returnNumber);
        Assert.Equal(ReturnStatus.InTransit, stored.Status);
        Assert.Equal("ups", stored.CarrierCode);
        Assert.Equal("1Z999AA10123456784", stored.TrackingNumber);
        Assert.Contains(stored.StatusHistory, h => h.ToStatus == ReturnStatus.InTransit);
    }

    [Fact]
    public async Task SignedInCustomer_WithdrawsReturnBeforeItProgresses()
    {
        var (email, userId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: userId);
        var client = await CustomerClientAsync(email);

        var returnNumber = await CreateReturnAsync(client, order);

        var detailHtml = await client.GetStringAsync($"/returns/{returnNumber}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(detailHtml);

        var cancel = await client.PostAsync($"/returns/{returnNumber}/cancel", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ReturnRequests.SingleAsync(r => r.ReturnNumber == returnNumber);
        Assert.Equal(ReturnStatus.Closed, stored.Status);
        Assert.True(stored.IsWithdrawn);
    }

    [Fact]
    public async Task SignedInCustomer_UploadsAttachments()
    {
        var (email, userId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: userId);
        var client = await CustomerClientAsync(email);

        var returnNumber = await CreateReturnAsync(client, order);

        var detailHtml = await client.GetStringAsync($"/returns/{returnNumber}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(detailHtml);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "files", "photo.jpg");

        var upload = await client.PostAsync($"/returns/{returnNumber}/attachments", content);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attachments = await db.ReturnAttachments.Where(a => a.ReturnRequest!.ReturnNumber == returnNumber).ToListAsync();
        Assert.Single(attachments);
    }

    // ---- Guest flows ----

    [Fact]
    public async Task Guest_CreatesReturn_WithSignedOrderTicket()
    {
        var email = UniqueEmail();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), guestEmail: email);
        var guestToken = await IssueGuestTokenAsync(order.PublicOrderNumber, email);

        var client = CreateClient();
        var startHtml = await client.GetStringAsync($"/returns/create/{order.PublicOrderNumber}?t={guestToken}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(startHtml);
        var orderItem = order.Items.First();

        var response = await client.PostAsync("/returns/create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["PublicOrderNumber"] = order.PublicOrderNumber,
            ["T"] = guestToken,
            ["ReasonCode"] = "Damaged",
            ["Items[0].OrderItemId"] = orderItem.Id.ToString(),
            ["Items[0].Quantity"] = "1"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = ExtractLocationPath(response);
        Assert.Contains("/returns/", location);
        Assert.False(string.IsNullOrEmpty(GetQueryValue(location, "t")));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ReturnRequests.SingleAsync(r => r.OrderId == order.Id);
        Assert.Equal(ReturnReasonCode.Damaged, stored.ReasonCode);
        Assert.Null(stored.UserId);
    }

    [Fact]
    public async Task Guest_WithoutTicket_IsRedirectedToTrack()
    {
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), guestEmail: UniqueEmail());
        var client = CreateClient();

        var response = await client.GetAsync($"/returns/create/{order.PublicOrderNumber}");

        AssertRedirectedTo(response, "/orders/track");
    }

    [Fact]
    public async Task Guest_CannotViewSignedInCustomersReturn()
    {
        var (_, userId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: userId);
        var client = CreateClient();

        var response = await client.GetAsync($"/returns/some-rma-number?order={order.PublicOrderNumber}&t=bad");

        AssertRedirectedTo(response, "/orders/track");
    }

    // ---- Full store round trip ----

    [Fact]
    public async Task EndToEnd_CustomerCreateAdminRefund_ClosesReturn()
    {
        var (email, userId) = await CreateCustomerAsync();
        var order = await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId: userId);
        var customer = await CustomerClientAsync(email);
        var admin = await AdminClientAsync();

        var returnNumber = await CreateReturnAsync(customer, order);

        Guid returnId;
        Guid itemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var returnRequest = await db.ReturnRequests.AsNoTracking().Include(r => r.Items).SingleAsync(r => r.ReturnNumber == returnNumber);
            returnId = returnRequest.Id;
            itemId = returnRequest.Items.First().Id;
        }

        var approve = await admin.PostAsync($"/api/admin/returns/{returnId}/approve", Json(new { note = "OK" }));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var detailHtml = await customer.GetStringAsync($"/returns/{returnNumber}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(detailHtml);
        var ship = await customer.PostAsync($"/returns/{returnNumber}/ship", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["carrierCode"] = "fedex",
            ["trackingNumber"] = "TRK-END-2-END"
        }));
        Assert.Equal(HttpStatusCode.Redirect, ship.StatusCode);

        var receive = await admin.PostAsync($"/api/admin/returns/{returnId}/receive", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);

        var inspect = await admin.PostAsync(
            $"/api/admin/returns/{returnId}/inspect",
            Json(new { resolution = "Refund", items = new[] { new { returnItemId = itemId, condition = "Sellable" } } }));
        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);

        var refund = await admin.PostAsync(
            $"/api/admin/returns/{returnId}/refund",
            Json(new { refundType = "Full", manual = true, idempotencyKey = $"e2e-{Guid.NewGuid():N}" }));
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);

        var complete = await admin.PostAsync($"/api/admin/returns/{returnId}/complete", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.ReturnRequests.AsNoTracking().SingleAsync(r => r.ReturnNumber == returnNumber);
            Assert.Equal(ReturnStatus.Closed, stored.Status);
            Assert.Equal(128m, stored.RefundedAmount);
        }
    }

    // ---- Helpers ----

    private static async Task<string> CreateReturnAsync(HttpClient client, Order order)
    {
        var startHtml = await client.GetStringAsync($"/returns/create/{order.PublicOrderNumber}");
        var token = CartTestsHelper.ExtractAntiforgeryToken(startHtml);
        var orderItem = order.Items.First();

        var response = await client.PostAsync("/returns/create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["PublicOrderNumber"] = order.PublicOrderNumber,
            ["ReasonCode"] = "ChangedMind",
            ["Items[0].OrderItemId"] = orderItem.Id.ToString(),
            ["Items[0].Quantity"] = "1"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var returnNumber = Path.GetFileName(ExtractLocationPath(response));
        Assert.False(string.IsNullOrEmpty(returnNumber));
        return returnNumber;
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
