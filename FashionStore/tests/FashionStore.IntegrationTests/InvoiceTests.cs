using System.Net;
using System.Security.Claims;
using System.Text;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Exercises the invoice surface end to end through the real web pipeline: the
/// admin invoice page and its permission gating, the customer invoice/PDF routes
/// (signed-in ownership and guest ticket flow), multi-page PDF generation for
/// large orders, snapshot rendering of colour/size/SKU, and partial-refund
/// display. Every invoice is generated from the order's immutable snapshots.
/// </summary>
public class InvoiceTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "AdminTest!pass1";
    private const string CustomerPassword = "PanelTest!pass1";

    private readonly WebApplicationFactory<Program> _factory;

    public InvoiceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"invoice-{Guid.NewGuid():N}@example.com";

    private static string UniqueOrderNumber() => $"ORD-I-{Guid.NewGuid():N}"[..24];

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

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString());
    }

    // ---- Seeding helpers ----

    private async Task<Order> SeedOrderAsync(
        string number,
        string? userId = null,
        string? guestEmail = null,
        OrderStatus status = OrderStatus.Placed,
        PaymentStatus payment = PaymentStatus.Unpaid,
        int itemCount = 1)
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
            PaidAmount = payment == PaymentStatus.Paid ? 137.99m : 0m,
            RefundedAmount = 0m,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard Delivery",
            OrderStatus = status,
            PaymentStatus = payment,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        for (var i = 0; i < itemCount; i++)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = CartTestsHelper.GetProductId(_factory, "cashmere-crew-neck-sweater"),
                ProductVariantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M"),
                ProductName = "Cashmere Crew Neck Sweater",
                ProductSlug = "cashmere-crew-neck-sweater",
                Sku = i == 0 ? "SW-1001-GREY-M" : $"SW-1001-GREY-M-{i:D2}",
                ColourName = "Heather Grey",
                ColourValue = "#999999",
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

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task SeedPaidPaymentAsync(Order order, string transactionId = "TXN-INV-000001")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payment = new Payment
        {
            OrderId = order.Id,
            ProviderCode = "card",
            PaymentMethodCode = "card",
            ProviderTransactionId = transactionId,
            IdempotencyKey = $"invoice-{order.Id:N}",
            Amount = order.GrandTotal,
            Currency = order.Currency,
            State = PaymentState.Paid,
            CreatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        };

        payment.Transactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id,
            Type = PaymentTransactionType.Capture,
            ProviderCode = "card",
            ProviderTransactionId = transactionId,
            Succeeded = true,
            ResultCode = "OK",
            ResultMessage = "Captured",
            CreatedAtUtc = DateTime.UtcNow
        });

        db.Payments.Add(payment);

        var refreshed = await db.Orders.SingleAsync(o => o.Id == order.Id);
        refreshed.PaidAmount = order.GrandTotal;
        refreshed.PaymentStatus = PaymentStatus.Paid;

        await db.SaveChangesAsync();
    }

    private async Task SeedPartialRefundAsync(Order order, decimal amount, string providerRefundId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);

        db.PaymentRefundRecords.Add(new PaymentRefundRecord
        {
            PaymentId = payment.Id,
            Amount = amount,
            Currency = "USD",
            ProviderRefundId = providerRefundId,
            Succeeded = true,
            CreatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        });

        var refreshed = await db.Orders.SingleAsync(o => o.Id == order.Id);
        refreshed.RefundedAmount = amount;
        refreshed.PaymentStatus = PaymentStatus.PartiallyPaid;

        await db.SaveChangesAsync();
    }

    private async Task<(string Email, string UserId)> CreateConfirmedUserAsync()
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
        var result = await userManager.CreateAsync(user, CustomerPassword);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return (email, user.Id);
    }

    private async Task<HttpClient> LoggedInClientAsync(string email, string password)
    {
        var client = CreateClient();
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var token = CartTestsHelper.ExtractAntiforgeryToken(loginHtml);
        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = password
            })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    /// <summary>Creates a user with permission claims but no role membership.</summary>
    private async Task<(string Email, string UserId)> CreateUserWithPermissionsAsync(params string[] permissions)
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

        foreach (var permission in permissions)
        {
            var claimResult = await userManager.AddClaimAsync(user, new Claim("permission", permission));
            Assert.True(claimResult.Succeeded, string.Join("; ", claimResult.Errors.Select(e => e.Description)));
        }

        return (email, user.Id);
    }

    /// <summary>Creates a user in the "Admin" role (creating the role if needed) with the given permission claims.</summary>
    private async Task<(string Email, string UserId)> CreateAdminRoleUserAsync(params string[] permissions)
    {
        var email = UniqueEmail();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (await roleManager.FindByNameAsync("Admin") is null)
        {
            var roleResult = await roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Admin",
                Description = "Administrative access with limited system settings",
                IsSystemRole = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

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

        var roleAddResult = await userManager.AddToRoleAsync(user, "Admin");
        Assert.True(roleAddResult.Succeeded, string.Join("; ", roleAddResult.Errors.Select(e => e.Description)));

        foreach (var permission in permissions)
        {
            var claimResult = await userManager.AddClaimAsync(user, new Claim("permission", permission));
            Assert.True(claimResult.Succeeded, string.Join("; ", claimResult.Errors.Select(e => e.Description)));
        }

        return (email, user.Id);
    }

    private async Task<HttpClient> AdminClientAsync(params string[] permissions)
    {
        var (email, _) = await CreateAdminRoleUserAsync(permissions);
        return await LoggedInClientAsync(email, Password);
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

    private static async Task<byte[]> ReadPdfBytesAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 8, "PDF response should contain a document");
        Assert.Equal(0x25, bytes[0]); // "%"
        Assert.Equal(0x50, bytes[1]); // "P"
        Assert.Equal(0x44, bytes[2]); // "D"
        Assert.Equal(0x46, bytes[3]); // "F"
        return bytes;
    }

    // ---- Admin invoice page ----

    [Fact]
    public async Task Admin_InvoicePage_RendersInvoiceNumberAndSnapshotItems()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), payment: PaymentStatus.Paid);

        var client = await AdminClientAsync("Orders.View", "Orders.PrintInvoice");
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("INV-", html);
        Assert.Contains(order.PublicOrderNumber, html);
        Assert.Contains("Cashmere Crew Neck Sweater", html);
        Assert.Contains("SW-1001-GREY-M", html);
        Assert.Contains("Heather Grey", html);
        Assert.Contains(">M</span>", html);
        Assert.Contains("Standard Delivery", html);
        Assert.Contains("137.99", html);
    }

    [Fact]
    public async Task Admin_InvoicePage_AnonymousUser_RedirectsToLogin()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = CreateClient();
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice");

        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task Admin_InvoicePage_UserWithoutAdminRole_RedirectsToAccessDenied()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var (email, _) = await CreateUserWithPermissionsAsync("Orders.View", "Orders.PrintInvoice");
        var client = await LoggedInClientAsync(email, Password);
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task Admin_InvoicePage_OrderWithRefund_ShowsRefundLineAndReference()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), payment: PaymentStatus.Paid);
        await SeedPaidPaymentAsync(order);
        await SeedPartialRefundAsync(order, 50m, "REF-INV-0001");

        var client = await AdminClientAsync("Orders.View", "Orders.PrintInvoice");
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Refunded", html);
        Assert.Contains("REF-INV-0001", html);
        Assert.Contains("50.00", html);
    }

    // ---- Admin invoice PDF ----

    [Fact]
    public async Task Admin_InvoicePdf_DownloadsValidPdf()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), payment: PaymentStatus.Paid);

        var client = await AdminClientAsync("Orders.View", "Orders.PrintInvoice");
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        await ReadPdfBytesAsync(response);
    }

    [Fact]
    public async Task Admin_InvoicePdf_WithoutPrintInvoicePermission_RedirectsToAccessDenied()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync("Orders.View");
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice.pdf");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task Admin_InvoicePdf_AnonymousUser_RedirectsToLogin()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = CreateClient();
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice.pdf");

        AssertRedirectedTo(response, "/Account/Login");
    }

    // ---- Customer signed-in ownership ----

    [Fact]
    public async Task Customer_Invoice_SignedInOwner_ShowsOwnInvoice()
    {
        var (email, userId) = await CreateConfirmedUserAsync();
        var order = await SeedOrderAsync(UniqueOrderNumber(), userId, email);

        var client = await LoggedInClientAsync(email, CustomerPassword);
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("INV-", html);
        Assert.Contains(order.PublicOrderNumber, html);
        Assert.Contains("Cashmere Crew Neck Sweater", html);
        Assert.Contains("Heather Grey", html);
        Assert.Contains("SW-1001-GREY-M", html);
    }

    [Fact]
    public async Task Customer_Invoice_SignedInOtherUsersOrder_RedirectsToTrack()
    {
        var (ownerEmail, ownerId) = await CreateConfirmedUserAsync();
        var order = await SeedOrderAsync(UniqueOrderNumber(), ownerId, ownerEmail);

        var (otherEmail, _) = await CreateConfirmedUserAsync();
        var client = await LoggedInClientAsync(otherEmail, CustomerPassword);
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}");

        AssertRedirectedTo(response, "/orders/track");
    }

    [Fact]
    public async Task Customer_Invoice_SignedInOwner_DownloadsPdf()
    {
        var (email, userId) = await CreateConfirmedUserAsync();
        var order = await SeedOrderAsync(UniqueOrderNumber(), userId, email);

        var client = await LoggedInClientAsync(email, CustomerPassword);
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        await ReadPdfBytesAsync(response);
    }

    // ---- Guest ticket flow ----

    [Fact]
    public async Task Customer_Invoice_GuestWithValidToken_ShowsInvoice()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        var order = await SeedOrderAsync(number, guestEmail: email);
        var guestToken = await IssueGuestTokenAsync(number, email);

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}?t={guestToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("INV-", html);
        Assert.Contains("Cashmere Crew Neck Sweater", html);
        Assert.Contains("Heather Grey", html);
    }

    [Fact]
    public async Task Customer_Invoice_GuestWithValidToken_DownloadsPdf()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        var order = await SeedOrderAsync(number, guestEmail: email);
        var guestToken = await IssueGuestTokenAsync(number, email);

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}/pdf?t={guestToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        await ReadPdfBytesAsync(response);
    }

    [Fact]
    public async Task Customer_Invoice_GuestWithoutToken_RedirectsToTrack()
    {
        var number = UniqueOrderNumber();
        var order = await SeedOrderAsync(number, guestEmail: UniqueEmail());

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}");

        AssertRedirectedTo(response, "/orders/track");
    }

    [Fact]
    public async Task Customer_Invoice_GuestWithInvalidToken_RedirectsToTrack()
    {
        var number = UniqueOrderNumber();
        var order = await SeedOrderAsync(number, guestEmail: UniqueEmail());

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}?t=not-a-valid-token");

        AssertRedirectedTo(response, "/orders/track");
    }

    [Fact]
    public async Task Customer_InvoicePdf_GuestWithValidToken_ReflectsPartialRefund()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        var order = await SeedOrderAsync(number, guestEmail: email, payment: PaymentStatus.Paid);
        await SeedPaidPaymentAsync(order);
        await SeedPartialRefundAsync(order, 40m, "REF-GUEST-77");
        var guestToken = await IssueGuestTokenAsync(number, email);

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}?t={guestToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Refunded", html);
        Assert.Contains("REF-GUEST-77", html);
        Assert.Contains("40.00", html);
    }

    // ---- Large orders / multi-page PDF ----

    [Fact]
    public async Task Admin_InvoicePdf_LargeOrder_GeneratesMultiPagePdf()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), payment: PaymentStatus.Paid, itemCount: 60);

        var client = await AdminClientAsync("Orders.View", "Orders.PrintInvoice");
        var response = await client.GetAsync($"/admin/orders/{order.Id}/invoice.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var pdf = await ReadPdfBytesAsync(response);

        // A 60-line-item invoice should span more than a single A4 page. QuestPDF
        // writes one "/Type /Page" object per page.
        var pdfText = Encoding.ASCII.GetString(pdf);
        var pageCount = System.Text.RegularExpressions.Regex.Matches(pdfText, @"/Type\s*/Page[^s]").Count;
        Assert.True(pageCount > 1, $"Expected a multi-page PDF but generated {pageCount} page(s).");
    }

    [Fact]
    public async Task Customer_Invoice_LargeOrder_ShowsAllSnapshotItems()
    {
        var number = UniqueOrderNumber();
        var email = UniqueEmail();
        var order = await SeedOrderAsync(number, guestEmail: email, itemCount: 25);
        var guestToken = await IssueGuestTokenAsync(number, email);

        var client = CreateClient();
        var response = await client.GetAsync($"/orders/invoice/{order.PublicOrderNumber}?t={guestToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("SW-1001-GREY-M-24", html);
        Assert.Contains("SW-1001-GREY-M-00", html);
    }
}
