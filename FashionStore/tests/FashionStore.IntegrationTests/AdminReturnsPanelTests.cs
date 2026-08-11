using System.Net;
using System.Net.Http.Json;
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
/// Exercises the administrative return-management surface end to end: permission
/// guarding on every lifecycle endpoint, filtered/paged listing, the detail payload
/// and the full refund and exchange state machine through the real services (stock
/// adjustment, manual + gateway refunds, exchange arrangement and closure). Role
/// permissions are granted as claims so 401/403 behaviour runs through the real
/// authorization pipeline.
/// </summary>
public class AdminReturnsPanelTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "AdminTest!pass1";

    private static readonly string[] AdminPermissions =
    {
        "Returns.View", "Returns.Review", "Returns.Inspect", "Returns.Restock",
        "Returns.Refund", "Returns.Exchange", "Returns.Complete"
    };

    private readonly WebApplicationFactory<Program> _factory;

    public AdminReturnsPanelTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"returnadmin-{Guid.NewGuid():N}@example.com";

    private static string UniqueOrderNumber() => $"ORD-R-{Guid.NewGuid():N}"[..24];

    private static string UniqueReturnNumber() => $"RMA-2026-{Guid.NewGuid():N}"[..18];

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static Guid GetGuid(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetGuid() : Guid.Empty;

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

    /// <summary>
    /// Seeds a delivered, paid order with the seeded sweater line, a captured
    /// payment and a return request at the given status (with items, history and,
    /// for in-transit returns, tracking details).
    /// </summary>
    private async Task<(Order Order, ReturnRequest Return)> SeedReturnAsync(
        string orderNumber,
        string returnNumber,
        ReturnStatus status,
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
            PublicOrderNumber = orderNumber,
            InvoiceNumber = orderNumber.Replace("ORD", "INV"),
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

        var orderItem = new OrderItem
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
        };
        order.Items.Add(orderItem);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            OrderId = order.Id,
            ProviderCode = "card",
            PaymentMethodCode = "card",
            ProviderTransactionId = $"TXN-R-{Guid.NewGuid():N}"[..20],
            IdempotencyKey = $"order-{order.Id:N}",
            Amount = order.GrandTotal,
            Currency = order.Currency,
            State = PaymentState.Paid,
            CreatedAtUtc = now.AddDays(-6),
            CompletedAtUtc = now.AddDays(-6)
        };
        payment.Transactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id,
            Type = PaymentTransactionType.Capture,
            ProviderCode = "card",
            ProviderTransactionId = payment.ProviderTransactionId,
            Succeeded = true,
            ResultCode = "OK",
            ResultMessage = "Captured",
            CreatedAtUtc = now.AddDays(-6)
        });
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var returnRequest = new ReturnRequest
        {
            ReturnNumber = returnNumber,
            OrderId = order.Id,
            UserId = userId,
            GuestEmail = guestEmail,
            CustomerName = "Jane Doe",
            Currency = "USD",
            Status = status,
            ReasonCode = ReturnReasonCode.ChangedMind,
            RefundableAmount = 128m,
            Resolution = status == ReturnStatus.Inspected ? ReturnResolution.Refund : ReturnResolution.None,
            TrackingNumber = status == ReturnStatus.InTransit ? "1ZRETURN999" : null,
            CarrierCode = status == ReturnStatus.InTransit ? "ups" : null,
            CreatedAtUtc = now.AddHours(-2),
            UpdatedAtUtc = now
        };
        returnRequest.Items.Add(new ReturnItem
        {
            ReturnRequestId = returnRequest.Id,
            OrderItemId = orderItem.Id,
            ProductId = productId,
            ProductVariantId = variantId,
            ProductName = orderItem.ProductName,
            Sku = orderItem.Sku,
            ColourName = orderItem.ColourName,
            ColourValue = orderItem.ColourValue,
            SizeName = orderItem.SizeName,
            ImageUrl = orderItem.ImageUrl,
            UnitPrice = orderItem.UnitPrice,
            Discount = orderItem.Discount,
            Tax = orderItem.Tax,
            Quantity = 1,
            PurchasedQuantity = 1,
            RefundableAmount = 128m,
            Condition = ReturnItemCondition.Undetermined
        });
        returnRequest.StatusHistory.Add(new ReturnStatusHistory
        {
            ReturnRequestId = returnRequest.Id,
            FromStatus = null,
            ToStatus = status,
            Note = "Seeded return",
            CreatedBy = "seed",
            CreatedAtUtc = returnRequest.CreatedAtUtc
        });
        db.ReturnRequests.Add(returnRequest);
        await db.SaveChangesAsync();

        return (order, returnRequest);
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
    public async Task Anonymous_CannotAccessAdminReturnsApi()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/admin/returns");

        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task UserWithoutReturnsViewPermission_GetsForbidden()
    {
        var client = await AdminClientAsync("Products.View");
        var response = await client.GetAsync("/api/admin/returns");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ViewerWithReturnsViewPermission_CanListReturns()
    {
        var orderNumber = UniqueOrderNumber();
        var returnNumber = UniqueReturnNumber();
        await SeedReturnAsync(orderNumber, returnNumber, ReturnStatus.Requested);

        var client = await AdminClientAsync("Returns.View");
        var response = await client.GetAsync("/api/admin/returns");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        var all = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(all, i => GetString(i, "returnNumber") == returnNumber);
    }

    [Fact]
    public async Task ViewerOnly_CannotPerformLifecycleActions()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Requested);

        var client = await AdminClientAsync("Returns.View");
        var response = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/approve",
            Json(new { }));

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ReviewerWithoutInspectPermission_CannotInspect()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Received);

        var client = await AdminClientAsync("Returns.View", "Returns.Review", "Returns.Restock", "Returns.Refund", "Returns.Exchange", "Returns.Complete");
        var response = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/inspect",
            Json(new { resolution = "Refund", items = Array.Empty<object>() }));

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ReviewerWithoutRefundPermission_CannotRefund()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Inspected);

        var client = await AdminClientAsync("Returns.View", "Returns.Review", "Returns.Inspect");
        var response = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/refund",
            Json(new { refundType = "Manual", amount = 128m, manual = true }));

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    // ---- List + search + filter ----

    [Fact]
    public async Task List_SearchByReturnNumber_FiltersResults()
    {
        var target = UniqueReturnNumber();
        var other = UniqueReturnNumber();
        await SeedReturnAsync(UniqueOrderNumber(), target, ReturnStatus.Requested);
        await SeedReturnAsync(UniqueOrderNumber(), other, ReturnStatus.Requested);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync($"/api/admin/returns?search={target}");
        var root = await ReadJsonAsync(response);

        var numbers = root.GetProperty("items").EnumerateArray().Select(i => GetString(i, "returnNumber")).ToList();
        Assert.Contains(target, numbers);
        Assert.DoesNotContain(other, numbers);
    }

    [Fact]
    public async Task List_FilterByStatus_OnlyReturnsMatchingStatus()
    {
        var requested = UniqueReturnNumber();
        var inTransit = UniqueReturnNumber();
        await SeedReturnAsync(UniqueOrderNumber(), requested, ReturnStatus.Requested);
        await SeedReturnAsync(UniqueOrderNumber(), inTransit, ReturnStatus.InTransit);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync($"/api/admin/returns?status={ReturnStatus.InTransit}");
        var root = await ReadJsonAsync(response);

        var numbers = root.GetProperty("items").EnumerateArray().Select(i => GetString(i, "returnNumber")).ToList();
        Assert.Contains(inTransit, numbers);
        Assert.DoesNotContain(requested, numbers);
    }

    [Fact]
    public async Task List_ReportsPagination()
    {
        for (var i = 0; i < 3; i++)
        {
            await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Requested);
        }

        var client = await AdminClientAsync(AdminPermissions);
        var firstPage = await ReadJsonAsync(await client.GetAsync("/api/admin/returns?page=1&pageSize=2"));
        Assert.Equal(2, firstPage.GetProperty("items").GetArrayLength());
        Assert.True(firstPage.GetProperty("hasMore").GetBoolean());
    }

    // ---- Detail ----

    [Fact]
    public async Task Detail_IncludesItemsTimelineAndOrderReference()
    {
        var orderNumber = UniqueOrderNumber();
        var returnNumber = UniqueReturnNumber();
        var (_, returnRequest) = await SeedReturnAsync(orderNumber, returnNumber, ReturnStatus.InTransit);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync($"/api/admin/returns/{returnRequest.Id}");
        var root = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(returnNumber, GetString(root, "returnNumber"));
        Assert.Equal(orderNumber, GetString(root, "orderNumber"));
        Assert.Equal("InTransit", GetString(root, "status"));
        Assert.Equal("1ZRETURN999", GetString(root, "trackingNumber"));

        Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal("Cashmere Crew Neck Sweater", GetString(root.GetProperty("items")[0], "productName"));
        Assert.Equal(128m, root.GetProperty("refundableAmount").GetDecimal());
        Assert.True(root.GetProperty("timeline").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Detail_UnknownReturn_ReturnsNotFound()
    {
        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.GetAsync($"/api/admin/returns/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Review / approve / reject ----

    [Fact]
    public async Task Review_ThenApprove_MovesThroughToAwaitingShipment()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Requested);

        var client = await AdminClientAsync(AdminPermissions);

        var review = await client.PostAsync($"/api/admin/returns/{returnRequest.Id}/review", Json(new { note = "Checking" }));
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewBody = await ReadJsonAsync(review);
        Assert.Equal("UnderReview", GetString(reviewBody, "status"));

        var approve = await client.PostAsync($"/api/admin/returns/{returnRequest.Id}/approve", Json(new { note = "Approved" }));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approveBody = await ReadJsonAsync(approve);
        Assert.Equal("AwaitingShipment", GetString(approveBody, "status"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ReturnRequests.Include(r => r.StatusHistory).SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.AwaitingShipment, stored.Status);
        Assert.NotNull(stored.ApprovedAtUtc);
        Assert.Equal(3, stored.StatusHistory.Count);
    }

    [Fact]
    public async Task Reject_RecordsRejectionDetails()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.UnderReview);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/reject",
            Json(new { reasonCode = "OutsideWindow", note = "Returned too late." }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Rejected", GetString(body, "status"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Rejected, stored.Status);
        Assert.Equal("OutsideWindow", stored.RejectionReasonCode);
        Assert.Equal("Returned too late.", stored.RejectionNote);
        Assert.NotNull(stored.RejectedAtUtc);
    }

    // ---- Full refund lifecycle ----

    [Fact]
    public async Task Lifecycle_RefundPath_RestockManualRefundAndComplete()
    {
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");
        Guid warehouseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            warehouseId = (await db.Warehouses.SingleAsync(w => w.IsDefault)).Id;
        }

        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.InTransit);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stockBefore = await db.WarehouseStocks.SingleAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId);
            Assert.Equal(10, stockBefore.OnHandQuantity);
        }

        var client = await AdminClientAsync(AdminPermissions);

        var receive = await client.PostAsync($"/api/admin/returns/{returnRequest.Id}/receive", Json(new { note = "Arrived" }));
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
        Assert.Equal("Received", GetString(await ReadJsonAsync(receive), "status"));

        var itemId = returnRequest.Items.First().Id;
        var inspect = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/inspect",
            Json(new { resolution = "Refund", items = new[] { new { returnItemId = itemId, condition = "Sellable" } }, note = "Good condition" }));
        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        Assert.Equal("Inspected", GetString(await ReadJsonAsync(inspect), "status"));

        var restock = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/restock",
            Json(new { returnItemId = itemId, warehouseId = warehouseId, note = "Restock" }));
        Assert.Equal(HttpStatusCode.OK, restock.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stockAfter = await db.WarehouseStocks.SingleAsync(s => s.ProductVariantId == variantId && s.WarehouseId == warehouseId);
            Assert.Equal(11, stockAfter.OnHandQuantity);
            var storedItem = await db.ReturnItems.SingleAsync(i => i.Id == itemId);
            Assert.True(storedItem.IsRestocked);
            Assert.NotNull(storedItem.RestockedAtUtc);
        }

        var refund = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/refund",
            Json(new { refundType = "Manual", amount = 128m, manual = true, idempotencyKey = $"key-{Guid.NewGuid():N}" }));
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        Assert.Equal("Refunded", GetString(await ReadJsonAsync(refund), "status"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedRefund = await db.Refunds.Include(rf => rf.Transactions).SingleAsync(rf => rf.ReturnRequestId == returnRequest.Id);
            Assert.Equal(RefundStatus.Succeeded, storedRefund.Status);
            Assert.Equal(128m, storedRefund.Amount);
            Assert.False(storedRefund.IsGatewayRefund);
            Assert.StartsWith("RFN-", storedRefund.ReferenceNumber);
        }

        var complete = await client.PostAsync($"/api/admin/returns/{returnRequest.Id}/complete", Json(new { note = "Done" }));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal("Closed", GetString(await ReadJsonAsync(complete), "status"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequest.Id);
            Assert.Equal(ReturnStatus.Closed, stored.Status);
            Assert.NotNull(stored.CompletedAtUtc);
            Assert.NotNull(stored.RefundedAtUtc);
        }
    }

    // ---- Gateway refund + idempotency ----

    [Fact]
    public async Task Refund_GatewayRefund_RefundsPaymentAndIsIdempotent()
    {
        var (order, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Inspected);
        var idempotencyKey = $"gw-{Guid.NewGuid():N}";

        var client = await AdminClientAsync(AdminPermissions);
        var payload = new { refundType = "Full", manual = false, idempotencyKey };

        var first = await client.PostAsync($"/api/admin/returns/{returnRequest.Id}/refund", Json(payload));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Refunded", GetString(await ReadJsonAsync(first), "status"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedRefund = await db.Refunds.Include(rf => rf.Transactions).SingleAsync(rf => rf.ReturnRequestId == returnRequest.Id);
            Assert.Equal(RefundStatus.Succeeded, storedRefund.Status);
            Assert.True(storedRefund.IsGatewayRefund);
            Assert.NotNull(storedRefund.ProviderRefundId);
            Assert.Equal(128m, storedRefund.Amount);

            var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
            Assert.Equal(PaymentState.PartiallyRefunded, payment.State);
        }

        var second = await client.PostAsync($"/api/admin/returns/{returnRequest.Id}/refund", Json(payload));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refunds = await db.Refunds.Where(rf => rf.ReturnRequestId == returnRequest.Id).ToListAsync();
            Assert.Single(refunds);
        }
    }

    [Fact]
    public async Task Refund_WithoutCapturedPayment_FailsForGatewayRefund()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Inspected);
        var returnId = returnRequest.Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payments = await db.Payments.Where(p => p.OrderId == returnRequest.OrderId).ToListAsync();
            db.Payments.RemoveRange(payments);
            await db.SaveChangesAsync();
        }

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/returns/{returnId}/refund",
            Json(new { refundType = "Full", manual = false, idempotencyKey = $"nogw-{Guid.NewGuid():N}" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Contains("manual refund", GetString(body, "error"), StringComparison.OrdinalIgnoreCase);
    }

    // ---- Exchange lifecycle ----

    [Fact]
    public async Task Lifecycle_ExchangePath_ArrangesExchangeAndCloses()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Inspected);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.ReturnRequests.SingleAsync(r => r.Id == returnRequest.Id);
            stored.Resolution = ReturnResolution.Exchange;
            await db.SaveChangesAsync();
        }

        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");
        var client = await AdminClientAsync(AdminPermissions);

        var exchange = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/exchange",
            Json(new { productVariantId = variantId, quantity = 1, note = "Replace" }));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        Assert.Equal("Exchanged", GetString(await ReadJsonAsync(exchange), "status"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.ExchangeRequests.SingleAsync(e => e.ReturnRequestId == returnRequest.Id);
            Assert.Equal(ExchangeStatus.Pending, stored.Status);
            Assert.Equal(1, stored.Quantity);
        }

        var complete = await client.PostAsync($"/api/admin/returns/{returnRequest.Id}/complete", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal("Closed", GetString(await ReadJsonAsync(complete), "status"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.ExchangeRequests.SingleAsync(e => e.ReturnRequestId == returnRequest.Id);
            Assert.Equal(ExchangeStatus.Completed, stored.Status);
            Assert.NotNull(stored.CompletedAtUtc);
        }
    }

    // ---- Notes ----

    [Fact]
    public async Task Notes_UpdatesAdminNotesInDetail()
    {
        var (_, returnRequest) = await SeedReturnAsync(UniqueOrderNumber(), UniqueReturnNumber(), ReturnStatus.Requested);

        var client = await AdminClientAsync(AdminPermissions);
        var response = await client.PostAsync(
            $"/api/admin/returns/{returnRequest.Id}/notes",
            Json(new { note = "Call customer to confirm sizes" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await ReadJsonAsync(await client.GetAsync($"/api/admin/returns/{returnRequest.Id}"));
        Assert.Contains("Call customer to confirm sizes", GetString(detail, "adminNotes"));
    }
}
