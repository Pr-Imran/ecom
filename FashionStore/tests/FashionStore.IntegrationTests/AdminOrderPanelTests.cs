using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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
/// Exercises the administrative order management surface end to end: permission
/// guarding on every endpoint, the filtered/paged list and CSV export, the detail
/// sections, the forward-only status state machine with its audit trail, shipment
/// tracking, internal vs customer notes and cancellation that releases stock and
/// voids coupon usage. Uses role-backed permission claims so 401/403 behaviour is
/// verified against the real authorization pipeline.
/// </summary>
public class AdminOrderPanelTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "AdminTest!pass1";

    private static readonly string[] AdminPermissions =
    {
        "Orders.View", "Orders.UpdateStatus", "Orders.Cancel", "Orders.AddNote", "Orders.PrintInvoice"
    };

    private readonly WebApplicationFactory<Program> _factory;

    public AdminOrderPanelTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"admin-{Guid.NewGuid():N}@example.com";

    private static string UniqueOrderNumber() => $"ORD-A-{Guid.NewGuid():N}"[..24];

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

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

    // ---- Seeding helpers ----

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

    private async Task<HttpClient> AdminClientAsync(params string[] permissions)
    {
        var (email, _) = await CreateUserWithPermissionsAsync(permissions);
        return await LoggedInClientAsync(email);
    }

    private async Task<HttpClient> LoggedInClientAsync(string email)
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

    private async Task<Order> SeedOrderAsync(
        string number,
        OrderStatus status = OrderStatus.Placed,
        PaymentStatus payment = PaymentStatus.Unpaid,
        FulfilmentStatus fulfilment = FulfilmentStatus.Unfulfilled,
        string? email = null,
        string? phone = null,
        string? customerName = null,
        decimal total = 137.99m,
        bool withHistory = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = new Order
        {
            PublicOrderNumber = number,
            InvoiceNumber = number.Replace("ORD", "INV"),
            GuestEmail = email,
            GuestPhone = phone ?? "555-0100",
            CustomerName = customerName ?? "Jane Doe",
            Currency = "USD",
            Subtotal = 128m,
            ProductDiscount = 0m,
            CouponDiscount = 0m,
            ShippingCharge = 9.99m,
            Tax = 0m,
            GrandTotal = total,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard Delivery",
            OrderStatus = status,
            PaymentStatus = payment,
            FulfilmentStatus = fulfilment,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        order.Items.Add(new OrderItem
        {
            ProductId = CartTestsHelper.GetProductId(_factory, "cashmere-crew-neck-sweater"),
            ProductVariantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M"),
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

        order.ShippingAddress = new OrderAddress
        {
            AddressType = OrderAddressType.Shipping,
            RecipientName = customerName ?? "Jane Doe",
            Phone = phone ?? "555-0100",
            AddressLine1 = "1 Main Street",
            City = "New York",
            Region = "NY",
            PostalCode = "10001",
            CountryCode = "US"
        };

        if (withHistory)
        {
            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = null,
                ToStatus = OrderStatus.Placed,
                Note = "Order placed",
                CreatedBy = "Checkout",
                CreatedAtUtc = order.CreatedAtUtc
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task SeedPaymentAsync(Order order, string transactionId = "TXN-ADMIN-000001")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payment = new Payment
        {
            OrderId = order.Id,
            ProviderCode = "card",
            PaymentMethodCode = "card",
            ProviderTransactionId = transactionId,
            IdempotencyKey = $"order-{order.Id:N}",
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
        await db.SaveChangesAsync();
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
            Status = StockReservationStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.StockReservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation;
    }

    private async Task<CouponUsage> SeedCouponUsageAsync(string number)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.Coupons.AnyAsync(c => c.Code == "ADMINCPN"))
        {
            db.Coupons.Add(new Coupon
            {
                Code = "ADMINCPN",
                NormalizedCode = "ADMINCPN",
                Name = "Admin 10%",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10m,
                IsActive = true,
                PerCustomerLimit = 5,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var coupon = await db.Coupons.SingleAsync(c => c.Code == "ADMINCPN");
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString());
    }

    // ---- Permission guards ----

    [Fact]
    public async Task Anonymous_CannotAccessAdminOrdersApi()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/admin/orders");

        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task UserWithoutOrdersViewPermission_GetsForbidden()
    {
        var client = await AdminClientAsync("Products.View");
        var response = await client.GetAsync("/api/admin/orders");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ViewerWithOrdersViewPermission_CanListOrders()
    {
        var number = UniqueOrderNumber();
        await SeedOrderAsync(number);

        var client = await AdminClientAsync("Orders.View");
        var response = await client.GetAsync("/api/admin/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        var all = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(all, i => GetString(i, "publicOrderNumber") == number);
    }

    [Fact]
    public async Task ViewerWithoutUpdateStatusPermission_CannotUpdateStatus()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync("Orders.View");
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/status",
            Json(new { toStatus = "Processing" }));

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ViewerWithoutAddNotePermission_CannotAddNote()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync("Orders.View");
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/notes",
            Json(new { note = "Nope", isInternal = true }));

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ViewerWithoutCancelPermission_CannotCancel()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync("Orders.View");
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/cancel",
            Json(new { reason = "DuplicateOrder" }));

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task Admin_CanUpdateStatus_WhenGranted()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/status",
            Json(new { toStatus = "Processing" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- List + filters + export ----

    [Fact]
    public async Task List_ReturnsSeededOrders()
    {
        var first = UniqueOrderNumber();
        var second = UniqueOrderNumber();
        await SeedOrderAsync(first, email: $"first-{Guid.NewGuid():N}@example.com");
        await SeedOrderAsync(second, email: $"second-{Guid.NewGuid():N}@example.com");

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync("/api/admin/orders?pageSize=50");
        var root = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var numbers = root.GetProperty("items")
            .EnumerateArray()
            .Select(i => GetString(i, "publicOrderNumber"))
            .ToList();
        Assert.Contains(first, numbers);
        Assert.Contains(second, numbers);
        Assert.True(root.GetProperty("totalCount").GetInt32() >= 2);
    }

    [Fact]
    public async Task List_SearchByOrderNumber_FiltersResults()
    {
        var target = UniqueOrderNumber();
        var other = UniqueOrderNumber();
        await SeedOrderAsync(target, email: $"t-{Guid.NewGuid():N}@example.com");
        await SeedOrderAsync(other, email: $"o-{Guid.NewGuid():N}@example.com");

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync($"/api/admin/orders?search={target}&pageSize=50");
        var root = await ReadJsonAsync(response);

        var numbers = root.GetProperty("items")
            .EnumerateArray()
            .Select(i => GetString(i, "publicOrderNumber"))
            .ToList();
        Assert.Contains(target, numbers);
        Assert.DoesNotContain(other, numbers);
    }

    [Fact]
    public async Task List_SearchByEmailAndPhone_FiltersResults()
    {
        var targetNumber = UniqueOrderNumber();
        var otherNumber = UniqueOrderNumber();
        var email = $"search-{Guid.NewGuid():N}@example.com";
        await SeedOrderAsync(targetNumber, email: email, phone: "555-4242");
        await SeedOrderAsync(otherNumber, email: $"other-{Guid.NewGuid():N}@example.com", phone: "555-0000");

        var client = await AdminClientAsync(AdminPermissions);

        var byEmail = await ReadJsonAsync(await client.GetAsync($"/api/admin/orders?search={Uri.EscapeDataString(email)}"));
        var byEmailNumbers = byEmail.GetProperty("items").EnumerateArray()
            .Select(i => GetString(i, "publicOrderNumber")).ToList();
        Assert.Contains(targetNumber, byEmailNumbers);
        Assert.DoesNotContain(otherNumber, byEmailNumbers);

        var byPhone = await ReadJsonAsync(await client.GetAsync("/api/admin/orders?search=555-4242"));
        var byPhoneNumbers = byPhone.GetProperty("items").EnumerateArray()
            .Select(i => GetString(i, "publicOrderNumber")).ToList();
        Assert.Contains(targetNumber, byPhoneNumbers);
    }

    [Fact]
    public async Task List_SearchByProviderTransactionId_FiltersResults()
    {
        var target = UniqueOrderNumber();
        var other = UniqueOrderNumber();
        var targetOrder = await SeedOrderAsync(target, email: $"t-{Guid.NewGuid():N}@example.com");
        await SeedOrderAsync(other, email: $"o-{Guid.NewGuid():N}@example.com");
        var transactionId = $"TXN-{Guid.NewGuid():N}"[..20];
        await SeedPaymentAsync(targetOrder, transactionId);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync($"/api/admin/orders?search={transactionId}");
        var root = await ReadJsonAsync(response);

        var numbers = root.GetProperty("items")
            .EnumerateArray()
            .Select(i => GetString(i, "publicOrderNumber"))
            .ToList();
        Assert.Contains(target, numbers);
        Assert.DoesNotContain(other, numbers);
    }

    [Fact]
    public async Task List_FilterByStatusAndPaymentStatus()
    {
        var shipped = UniqueOrderNumber();
        var placed = UniqueOrderNumber();
        await SeedOrderAsync(shipped, status: OrderStatus.Shipped, payment: PaymentStatus.Paid);
        await SeedOrderAsync(placed, status: OrderStatus.Placed);

        var client = await AdminClientAsync(AdminPermissions);

        var byStatus = await ReadJsonAsync(await client.GetAsync("/api/admin/orders?orderStatus=Shipped"));
        var byStatusNumbers = byStatus.GetProperty("items").EnumerateArray()
            .Select(i => GetString(i, "publicOrderNumber")).ToList();
        Assert.Contains(shipped, byStatusNumbers);
        Assert.DoesNotContain(placed, byStatusNumbers);

        var byPayment = await ReadJsonAsync(await client.GetAsync("/api/admin/orders?paymentStatus=Paid"));
        var byPaymentNumbers = byPayment.GetProperty("items").EnumerateArray()
            .Select(i => GetString(i, "publicOrderNumber")).ToList();
        Assert.Contains(shipped, byPaymentNumbers);
        Assert.DoesNotContain(placed, byPaymentNumbers);
    }

    [Fact]
    public async Task List_Pagination_ReportsHasMore()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedOrderAsync(UniqueOrderNumber(), email: $"pg-{Guid.NewGuid():N}@example.com");
        }

        var client = await AdminClientAsync(AdminPermissions);
        var firstPage = await ReadJsonAsync(await client.GetAsync("/api/admin/orders?page=1&pageSize=2"));
        Assert.Equal(2, firstPage.GetProperty("items").GetArrayLength());
        Assert.True(firstPage.GetProperty("hasMore").GetBoolean());

        var totalCount = firstPage.GetProperty("totalCount").GetInt32();
        var lastPageNumber = (totalCount + 1) / 2;
        var lastPage = await ReadJsonAsync(await client.GetAsync($"/api/admin/orders?page={lastPageNumber}&pageSize=2"));
        Assert.False(lastPage.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Export_ReturnsCsvWithHeaderAndRows()
    {
        var number = UniqueOrderNumber();
        await SeedOrderAsync(number, email: $"csv-{Guid.NewGuid():N}@example.com");

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync("/api/admin/orders/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("OrderNumber,InvoiceNumber,Customer,Email,Phone,IsGuest,", csv);
        Assert.Contains(number, csv);
        Assert.Contains("Jane Doe", csv);
    }

    [Fact]
    public async Task Export_RespectsFilters()
    {
        var target = UniqueOrderNumber();
        var other = UniqueOrderNumber();
        await SeedOrderAsync(target, email: $"x-{Guid.NewGuid():N}@example.com");
        await SeedOrderAsync(other, email: $"y-{Guid.NewGuid():N}@example.com");

        var client = await AdminClientAsync(AdminPermissions);
        var csv = await (await client.GetAsync($"/api/admin/orders/export?search={target}")).Content.ReadAsStringAsync();

        Assert.Contains(target, csv);
        Assert.DoesNotContain(other, csv);
    }

    // ---- Detail sections ----

    [Fact]
    public async Task Detail_IncludesAllSections()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());
        await SeedPaymentAsync(order, "TXN-DETAIL-1");

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync($"/api/admin/orders/{order.Id}");
        var root = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(order.PublicOrderNumber, GetString(root, "publicOrderNumber"));
        Assert.True(root.GetProperty("isGuest").GetBoolean());
        Assert.Equal("Placed", GetString(root, "orderStatus"));
        Assert.Equal("Unpaid", GetString(root, "paymentStatus"));

        Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal("Cashmere Crew Neck Sweater", GetString(root.GetProperty("items")[0], "productName"));
        Assert.Equal("1 Main Street", GetString(root.GetProperty("shippingAddress"), "addressLine1"));

        Assert.True(root.GetProperty("statusHistory").GetArrayLength() >= 1);
        Assert.Contains(root.GetProperty("statusHistory").EnumerateArray(),
            h => GetString(h, "toStatus") == "Placed");

        Assert.Single(root.GetProperty("paymentTransactions").EnumerateArray());
        Assert.Equal("Capture", GetString(root.GetProperty("paymentTransactions")[0], "type"));

        Assert.True(root.GetProperty("canCancel").GetBoolean());
        Assert.True(root.GetProperty("canProcess").GetBoolean());
        Assert.False(root.GetProperty("canShip").GetBoolean());
    }

    [Fact]
    public async Task Detail_UnknownOrder_ReturnsNotFound()
    {
        var client = await AdminClientAsync(AdminPermissions);

        var response = await client.GetAsync($"/api/admin/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Forward transition + history ----

    [Fact]
    public async Task Status_ForwardTransitions_RecordHistory()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync(AdminPermissions);

        foreach (var (toStatus, expected) in new[]
                 {
                     ("Confirmed", "Confirmed"),
                     ("Processing", "Processing"),
                     ("Shipped", "Shipped"),
                     ("Delivered", "Delivered")
                 })
        {
            var response = await client.PostAsync(
                $"/api/admin/orders/{order.Id}/status",
                Json(new { toStatus }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await ReadJsonAsync(response);
            Assert.True(body.GetProperty("success").GetBoolean());
            Assert.Equal(expected, GetString(body, "orderStatus"));
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Delivered, stored.OrderStatus);
        Assert.NotNull(stored.ShippedAtUtc);
        Assert.NotNull(stored.DeliveredAtUtc);
        Assert.Equal(FulfilmentStatus.Fulfilled, stored.FulfilmentStatus);
        Assert.Equal(5, stored.StatusHistory.Count);
        Assert.Contains(stored.StatusHistory, h => h.ToStatus == OrderStatus.Shipped);
    }

    [Fact]
    public async Task Status_BackwardsTransition_IsRefused()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Processing);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/status",
            Json(new { toStatus = "Placed" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Contains("backwards", GetString(body, "error"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Status_ToCancelled_IsRefused()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/status",
            Json(new { toStatus = "Cancelled" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Contains("cancel action", GetString(body, "error"), StringComparison.OrdinalIgnoreCase);
    }

    // ---- Pack / ship / deliver ----

    [Fact]
    public async Task Pack_RequiresProcessingOrder()
    {
        var placed = await SeedOrderAsync(UniqueOrderNumber());
        var processing = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Processing);

        var client = await AdminClientAsync(AdminPermissions);

        var refused = await client.PostAsync($"/api/admin/orders/{placed.Id}/pack", Json(new { }));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var accepted = await client.PostAsync($"/api/admin/orders/{processing.Id}/pack", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Orders.SingleAsync(o => o.Id == processing.Id);
        Assert.NotNull(stored.PackedAtUtc);
    }

    [Fact]
    public async Task Ship_SetsTrackingAndMarksShippedFulfilled()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Processing);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/ship",
            Json(new { carrierCode = "ups", trackingNumber = "1Z999AA10123456784", trackingUrl = "https://www.ups.com/track?num=1Z999AA10123456784" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Shipped", GetString(body, "orderStatus"));
        Assert.Equal("Fulfilled", GetString(body, "fulfilmentStatus"));
        Assert.Equal("1Z999AA10123456784", GetString(body, "trackingNumber"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Shipped, stored.OrderStatus);
        Assert.Equal(FulfilmentStatus.Fulfilled, stored.FulfilmentStatus);
        Assert.Equal("ups", stored.CarrierCode);
        Assert.Equal("1Z999AA10123456784", stored.TrackingNumber);
        Assert.NotNull(stored.ShippedAtUtc);
    }

    [Fact]
    public async Task Ship_NonProcessingOrder_IsRefused()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Placed);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/ship",
            Json(new { trackingNumber = "TRK-1" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deliver_RequiresShippedOrder()
    {
        var shipped = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Shipped);
        var processing = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Processing);

        var client = await AdminClientAsync(AdminPermissions);

        var refused = await client.PostAsync($"/api/admin/orders/{processing.Id}/deliver", Json(new { }));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var accepted = await client.PostAsync($"/api/admin/orders/{shipped.Id}/deliver", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var body = await ReadJsonAsync(accepted);
        Assert.Equal("Delivered", GetString(body, "orderStatus"));
    }

    // ---- Notes: internal vs customer ----

    [Fact]
    public async Task Notes_InternalAndCustomer_SplitInDetail()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync(AdminPermissions);

        var internalResponse = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/notes",
            Json(new { note = "Call customer about delayed delivery", isInternal = true }));
        Assert.Equal(HttpStatusCode.OK, internalResponse.StatusCode);

        var customerResponse = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/notes",
            Json(new { note = "Thanks for your patience", isInternal = false }));
        Assert.Equal(HttpStatusCode.OK, customerResponse.StatusCode);

        var detail = await ReadJsonAsync(await client.GetAsync($"/api/admin/orders/{order.Id}"));
        var internalNotes = detail.GetProperty("internalNotes").EnumerateArray().Select(n => GetString(n, "note")).ToList();
        var customerNotes = detail.GetProperty("customerNotes").EnumerateArray().Select(n => GetString(n, "note")).ToList();

        Assert.Contains("Call customer about delayed delivery", internalNotes);
        Assert.DoesNotContain("Thanks for your patience", internalNotes);
        Assert.Contains("Thanks for your patience", customerNotes);
        Assert.DoesNotContain("Call customer about delayed delivery", customerNotes);
    }

    [Fact]
    public async Task Notes_EmptyOrTooLong_IsRefused()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber());

        var client = await AdminClientAsync(AdminPermissions);

        var empty = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/notes",
            Json(new { note = "   ", isInternal = true }));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooLong = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/notes",
            Json(new { note = new string('x', 2001), isInternal = true }));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    // ---- Cancellation: release + void ----

    [Fact]
    public async Task Cancel_PlacedUnpaidOrder_ReleasesStockVoidsCouponAndRecordsHistory()
    {
        var number = UniqueOrderNumber();
        var order = await SeedOrderAsync(number);
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");
        var reservation = await SeedReservationAsync(number, variantId);
        var usage = await SeedCouponUsageAsync(number);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/cancel",
            Json(new { reason = "DuplicateOrder" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Cancelled", GetString(body, "orderStatus"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, stored.OrderStatus);
        Assert.NotNull(stored.CancelledAtUtc);
        Assert.Equal(OrderCancellationReason.DuplicateOrder.ToString(), stored.CancelledReasonCode);
        Assert.Contains(stored.StatusHistory, h => h.ToStatus == OrderStatus.Cancelled);

        var released = await db.StockReservations.SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(StockReservationStatus.Released, released.Status);

        var voided = await db.CouponUsages.SingleAsync(u => u.Id == usage.Id);
        Assert.NotNull(voided.VoidedAtUtc);
    }

    [Fact]
    public async Task Cancel_PaidOrder_IsRefused()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Placed, payment: PaymentStatus.Paid);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/cancel",
            Json(new { reason = "ChangedMind" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Contains("refund", GetString(body, "error"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_ProgressedOrder_IsRefused()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Processing);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/cancel",
            Json(new { reason = "ChangedMind" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Processing, stored.OrderStatus);
        Assert.Null(stored.CancelledAtUtc);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_IsRefused()
    {
        var order = await SeedOrderAsync(UniqueOrderNumber(), status: OrderStatus.Cancelled);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/orders/{order.Id}/cancel",
            Json(new { reason = "ChangedMind" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
