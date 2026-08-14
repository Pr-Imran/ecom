using System.Net;
using System.Security.Claims;
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
/// Phase 28 administration dashboard and reports. The HTML page / authorization
/// and the report API plumbing (filter options, unknown-type, export lifecycle)
/// run against the shared InMemory host. Report aggregation runs against the
/// SQLite-backed <see cref="SqliteReportTestFactory"/> because the GroupBy
/// projections used by the report services do not translate on EF InMemory.
/// </summary>
public class AdminDashboardAndReportsPageTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "AdminTest!pass1";

    private static readonly int[] AcceptedExportStatuses = { 0, 2 };

    private readonly TestWebApplicationFactory _factory;

    public AdminDashboardAndReportsPageTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"admin-dash-{Guid.NewGuid():N}@example.com";

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString());
    }

    private async Task<(string Email, string UserId)> CreateAdminRoleUserAsync(params string[] permissions)
    {
        var email = UniqueEmail();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (await roleManager.FindByNameAsync("Admin") is null)
        {
            await roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Admin",
                Description = "Administrative access",
                IsSystemRole = true,
                CreatedAtUtc = DateTime.UtcNow
            });
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
        await userManager.AddToRoleAsync(user, "Admin");

        foreach (var permission in permissions)
        {
            await userManager.AddClaimAsync(user, new Claim("permission", permission));
        }

        return (email, user.Id);
    }

    private async Task<HttpClient> LoggedInClientAsync(string email)
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
                ["Password"] = Password
            })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static JsonElement ReadJson(HttpResponseMessage response)
    {
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    // ---- Dashboard page ----

    [Fact]
    public async Task Dashboard_Anonymous_RedirectsToLogin()
    {
        var response = await CreateClient().GetAsync("/admin");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task Dashboard_AdminWithoutDashboardView_RedirectsToAccessDenied()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Orders.View");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/admin");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task Dashboard_AdminWithDashboardView_ReturnsOkWithMetrics()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Dashboard.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Sales today", html);
        Assert.Contains("Sales this month", html);
        Assert.Contains("Average order", html);
        Assert.Contains("Recent orders", html);
        Assert.Contains("Sales trend", html);
    }

    // ---- Reports page ----

    [Fact]
    public async Task Reports_Anonymous_RedirectsToLogin()
    {
        var response = await CreateClient().GetAsync("/admin/reports");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task Reports_AdminWithoutReportsView_RedirectsToAccessDenied()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Dashboard.View");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/admin/reports");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task Reports_AdminWithReportsView_ReturnsOk()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/admin/reports");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Export CSV", html);
        Assert.Contains("sales", html);
    }

    // ---- Reports API ----

    [Fact]
    public async Task ReportsApi_Anonymous_RedirectsToLogin()
    {
        var response = await CreateClient().GetAsync("/api/admin/reports/filters");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task ReportsApi_WithoutReportsView_RedirectsToAccessDenied()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Dashboard.View");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/api/admin/reports/filters");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ReportsApi_Filters_ReturnsFilterOptions()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/reports/filters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = ReadJson(response);
        foreach (var property in new[] { "categories", "brands", "products", "paymentMethods", "shippingMethods", "customers", "currencies" })
        {
            Assert.True(root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array, property);
        }
    }

    [Fact]
    public async Task ReportsApi_UnknownReportType_ReturnsNotFound()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/reports/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Export lifecycle ----

    [Fact]
    public async Task Export_Request_ReturnsJobId()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.PostAsync("/api/admin/reports/sales/export", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = ReadJson(response);
        var jobId = root.GetProperty("jobId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jobId));
        // Preparing (0) when the background storage accepts the job, or Failed (2)
        // when Hangfire storage is unavailable in the test host.
        Assert.Contains(root.GetProperty("status").GetInt32(), AcceptedExportStatuses);
    }

    [Fact]
    public async Task Export_Status_UnknownJob_ReturnsNotFound()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync($"/api/admin/reports/export/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_Download_UnknownJob_ReturnsNotFound()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync($"/api/admin/reports/export/{Guid.NewGuid():N}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_Download_BeforeReady_ReturnsNotFound()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var request = await client.PostAsync("/api/admin/reports/sales/export", null);
        var jobId = ReadJson(request).GetProperty("jobId").GetString();

        var response = await client.GetAsync($"/api/admin/reports/export/{jobId}/download");

        // The file is never ready synchronously, so the download is unavailable.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>
/// Report aggregation against the SQLite-backed host. Each test seeds orders into
/// a unique UTC date range so assertions stay isolated from other tests sharing
/// the database.
/// </summary>
public class AdminReportAggregationTests : IClassFixture<SqliteReportTestFactory>
{
    private const string Password = "AdminTest!pass1";

    private readonly SqliteReportTestFactory _factory;

    public AdminReportAggregationTests(SqliteReportTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"admin-aggr-{Guid.NewGuid():N}@example.com";

    private async Task<(string Email, string UserId)> CreateAdminRoleUserAsync(params string[] permissions)
    {
        var email = UniqueEmail();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (await roleManager.FindByNameAsync("Admin") is null)
        {
            await roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Admin",
                Description = "Administrative access",
                IsSystemRole = true,
                CreatedAtUtc = DateTime.UtcNow
            });
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
        await userManager.AddToRoleAsync(user, "Admin");

        foreach (var permission in permissions)
        {
            await userManager.AddClaimAsync(user, new Claim("permission", permission));
        }

        return (email, user.Id);
    }

    private async Task<HttpClient> LoggedInClientAsync(string email)
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
                ["Password"] = Password
            })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static JsonElement ReadJson(HttpResponseMessage response)
    {
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private async Task SeedOrderAsync(
        string number,
        decimal grandTotal,
        DateTime createdAtUtc,
        PaymentStatus payment = PaymentStatus.Paid,
        OrderStatus orderStatus = OrderStatus.Placed,
        decimal refunded = 0m,
        bool withItem = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = new Order
        {
            PublicOrderNumber = number,
            Currency = "USD",
            Subtotal = grandTotal,
            ShippingCharge = 0m,
            Tax = 0m,
            GrandTotal = grandTotal,
            PaidAmount = payment == PaymentStatus.Unpaid ? 0m : grandTotal,
            RefundedAmount = refunded,
            PaymentMethodCode = "card",
            PaymentStatus = payment,
            OrderStatus = orderStatus,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        if (withItem)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = SqliteReportTestFactory.SeededProductId,
                ProductName = "Cashmere Crew Neck Sweater",
                ProductSlug = "cashmere-crew-neck-sweater",
                Sku = "SW-1001",
                UnitPrice = grandTotal,
                Quantity = 1,
                LineTotal = grandTotal
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SalesReport_TotalsIncludePaidExcludeCancelledUnpaid_AndApplyRefunds()
    {
        var day = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        await SeedOrderAsync("AGGR-P1", 100m, day);
        await SeedOrderAsync("AGGR-C1", 50m, day, orderStatus: OrderStatus.Cancelled);
        await SeedOrderAsync("AGGR-U1", 25m, day, payment: PaymentStatus.Unpaid);
        await SeedOrderAsync("AGGR-R1", 200m, day, refunded: 40m);

        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/reports/sales?from=2026-08-10&to=2026-08-10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var totals = ReadJson(response).GetProperty("totals");
        Assert.Equal(2, totals.GetProperty("orderCount").GetInt32());
        Assert.Equal(300m, totals.GetProperty("grossSales").GetDecimal());
        Assert.Equal(40m, totals.GetProperty("refunds").GetDecimal());
        Assert.Equal(260m, totals.GetProperty("netSales").GetDecimal());
    }

    [Fact]
    public async Task SalesReport_RespectsExclusiveDateBoundary()
    {
        await SeedOrderAsync("AGGR-B1", 10m, new DateTime(2026, 8, 11, 23, 59, 59, DateTimeKind.Utc));
        await SeedOrderAsync("AGGR-B2", 20m, new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc));
        await SeedOrderAsync("AGGR-B3", 30m, new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc));
        await SeedOrderAsync("AGGR-B4", 40m, new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));

        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/reports/sales?from=2026-08-12&to=2026-08-14");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var totals = ReadJson(response).GetProperty("totals");
        Assert.Equal(2, totals.GetProperty("orderCount").GetInt32());
        Assert.Equal(50m, totals.GetProperty("grossSales").GetDecimal());
    }

    [Fact]
    public async Task OrderReport_ListsEveryStatusInRange()
    {
        var day = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
        await SeedOrderAsync("AGGR-O1", 100m, day);
        await SeedOrderAsync("AGGR-O2", 50m, day, orderStatus: OrderStatus.Cancelled);
        await SeedOrderAsync("AGGR-O3", 25m, day, payment: PaymentStatus.Unpaid);

        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/reports/orders?from=2026-08-20&to=2026-08-21&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = ReadJson(response);
        Assert.Equal(3, root.GetProperty("paging").GetProperty("totalCount").GetInt32());
        Assert.Contains("Cancelled", root.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("orderStatus").GetString()));
    }

    [Fact]
    public async Task ProductSalesReport_ExcludesCancelledOrders()
    {
        var day = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);
        await SeedOrderAsync("AGGR-Q1", 100m, day, withItem: true);
        await SeedOrderAsync("AGGR-Q2", 200m, day, orderStatus: OrderStatus.Cancelled, withItem: true);

        var (email, _) = await CreateAdminRoleUserAsync("Reports.View");
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/reports/product-sales?from=2026-08-25&to=2026-08-26");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = ReadJson(response);
        Assert.Equal(1, root.GetProperty("paging").GetProperty("totalCount").GetInt32());
        var row = root.GetProperty("items")[0];
        Assert.Equal(1, row.GetProperty("unitsSold").GetInt32());
        Assert.Equal(100m, row.GetProperty("grossRevenue").GetDecimal());
    }
}
