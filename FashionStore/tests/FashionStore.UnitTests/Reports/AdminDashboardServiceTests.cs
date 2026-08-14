using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Settings;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionStore.UnitTests.Reports;

public class AdminDashboardServiceTests
{
    private static WebsiteSettingsSnapshot Snapshot(string timezone = "UTC", string currency = "USD") =>
        new(
            new StoreSection("FashionStore", string.Empty, string.Empty),
            new BrandingSection(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty),
            new ContactSection(string.Empty, string.Empty, string.Empty),
            new CommerceSection(currency, "$", timezone, 30, "INV", 5, "admin@example.com"),
            new CheckoutSection(true, true, true),
            new OrderSection("ORD", "RMA"),
            new SeoSettingsSection(string.Empty, string.Empty),
            new MaintenanceSection(false, string.Empty),
            new ReviewsSection(true, true, 3));

    private static AppDbContext CreateContext() => SqliteTestContext.Create();

    private static MemoryDistributedCache CreateCache() =>
        new(new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static Mock<IWebsiteSettingsService> CreateSettingsMock(WebsiteSettingsSnapshot snapshot)
    {
        var mock = new Mock<IWebsiteSettingsService>();
        mock.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        return mock;
    }

    private static AdminDashboardService CreateService(
        AppDbContext context,
        IDistributedCache cache,
        Mock<IWebsiteSettingsService>? settings = null)
        => new(
            context,
            (settings ?? CreateSettingsMock(Snapshot())).Object,
            cache,
            NullLogger<AdminDashboardService>.Instance);

    private static Order CreateOrder(
        string number,
        decimal grandTotal,
        DateTime createdAtUtc,
        PaymentStatus paymentStatus = PaymentStatus.Paid,
        OrderStatus orderStatus = OrderStatus.Placed,
        decimal refundedAmount = 0m,
        string currency = "USD")
        => new()
        {
            PublicOrderNumber = number,
            Currency = currency,
            Subtotal = grandTotal,
            ShippingCharge = 0m,
            Tax = 0m,
            GrandTotal = grandTotal,
            PaidAmount = paymentStatus == PaymentStatus.Unpaid ? 0m : grandTotal,
            RefundedAmount = refundedAmount,
            PaymentStatus = paymentStatus,
            OrderStatus = orderStatus,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

    [Fact]
    public async Task GetDashboardAsync_CalculatesMetricsAcrossBoundedProjections()
    {
        var context = CreateContext();
        var now = DateTime.UtcNow;

        context.Orders.AddRange(
            CreateOrder("ORD-1", 100m, now),                                       // today, paid
            CreateOrder("ORD-2", 50m, now, orderStatus: OrderStatus.Cancelled),    // cancelled -> excluded from sales
            CreateOrder("ORD-3", 25m, now, paymentStatus: PaymentStatus.Unpaid),   // unpaid -> excluded from sales
            CreateOrder("ORD-4", 200m, now, refundedAmount: 40m),                  // refunded but still gross
            CreateOrder("ORD-5", 80m, now.AddDays(-40)));                          // outside month -> not in month sales

        context.Payments.Add(new Payment
        {
            OrderId = Guid.NewGuid(),
            ProviderCode = "manual",
            PaymentMethodCode = "card",
            IdempotencyKey = Guid.NewGuid().ToString(),
            Amount = 10m,
            Currency = "USD",
            State = PaymentState.Failed,
            CreatedAtUtc = now
        });

        var returned = new ReturnRequest
        {
            ReturnNumber = "RMA-1",
            OrderId = Guid.NewGuid(),
            Currency = "USD",
            Status = ReturnStatus.Requested,
            RefundableAmount = 20m,
            CreatedAtUtc = now
        };
        context.ReturnRequests.Add(returned);

        context.Refunds.Add(new Refund
        {
            ReturnRequestId = returned.Id,
            OrderId = returned.OrderId,
            ReferenceNumber = "REF-1",
            Type = RefundType.Full,
            Status = RefundStatus.Pending,
            Amount = 20m,
            Currency = "USD",
            IdempotencyKey = Guid.NewGuid().ToString(),
            CreatedAtUtc = now
        });

        context.Users.Add(new ApplicationUser
        {
            UserName = "new-customer@example.com",
            Email = "new-customer@example.com",
            CreatedAtUtc = now
        });

        await context.SaveChangesAsync();

        var service = CreateService(context, CreateCache());
        var data = await service.GetDashboardAsync(CancellationToken.None);

        // Sales today = 100 (ORD-1) + 200 (ORD-4). Cancelled/unpaid excluded.
        Assert.Equal(300m, data.Metrics.SalesToday);
        // Month sales same as today in this scenario (ORD-5 is outside the month).
        Assert.Equal(300m, data.Metrics.SalesThisMonth);
        // Orders today counts every order regardless of status (4 orders placed today).
        Assert.Equal(4, data.Metrics.OrdersToday);
        // Pending orders = placed/confirmed/processing/shipped (ORD-2 cancelled is excluded).
        Assert.Equal(4, data.Metrics.PendingOrders);
        // Paid (all-time, fully paid).
        Assert.Equal(4, data.Metrics.PaidOrders);
        // Failed payments this month.
        Assert.Equal(1, data.Metrics.FailedPayments);
        // Pending returns/refunds.
        Assert.Equal(1, data.Metrics.PendingReturns);
        Assert.Equal(1, data.Metrics.PendingRefunds);
        // New customers this month.
        Assert.Equal(1, data.Metrics.NewCustomers);
        // AOV = 300 / 2 paid non-cancelled orders this month.
        Assert.Equal(150m, data.Metrics.AverageOrderValue);
        Assert.Equal("USD", data.Metrics.Currency);
    }

    [Fact]
    public async Task GetDashboardAsync_RefundedOrderRemainsInGrossSales()
    {
        var context = CreateContext();
        context.Orders.Add(CreateOrder("ORD-R1", 250m, DateTime.UtcNow, refundedAmount: 100m));
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateCache());
        var data = await service.GetDashboardAsync(CancellationToken.None);

        Assert.Equal(250m, data.Metrics.SalesToday);
    }

    [Fact]
    public async Task GetDashboardAsync_ComputesStockCountsFromWarehouseRows()
    {
        var context = CreateContext();
        var lowVariant = new ProductVariant { Sku = "LOW-1", Price = 10m, IsActive = true };
        var emptyVariant = new ProductVariant { Sku = "EMPTY-1", Price = 10m, IsActive = true };
        context.ProductVariants.AddRange(lowVariant, emptyVariant);

        context.WarehouseStocks.AddRange(
            new WarehouseStock { WarehouseId = Guid.NewGuid(), ProductVariantId = lowVariant.Id, OnHandQuantity = 2, ReservedQuantity = 0, LowStockThreshold = 5 },
            new WarehouseStock { WarehouseId = Guid.NewGuid(), ProductVariantId = emptyVariant.Id, OnHandQuantity = 0, ReservedQuantity = 0 });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateCache());
        var data = await service.GetDashboardAsync(CancellationToken.None);

        Assert.Equal(1, data.Metrics.LowStockVariants);
        Assert.Equal(1, data.Metrics.OutOfStockVariants);
    }

    [Fact]
    public async Task GetDashboardAsync_ReportsTopProductsCategoriesAndBrands()
    {
        var context = CreateContext();
        var category = new Category { Name = "Clothing", Slug = "clothing", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        var brand = new Brand { Name = "Everlane", Slug = "everlane", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        var product = new Product
        {
            Name = "Cashmere Crew Neck Sweater",
            Slug = "cashmere-crew-neck-sweater",
            BaseSku = "SW-1001",
            BasePrice = 128m,
            Category = category,
            Brand = brand,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(product);

        var order = CreateOrder("ORD-TOP1", 128m, DateTime.UtcNow);
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id,
            ProductName = product.Name,
            ProductSlug = product.Slug,
            Sku = "SW-1001-GREY-M",
            UnitPrice = 128m,
            Quantity = 2,
            LineTotal = 256m
        });
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateCache());
        var data = await service.GetDashboardAsync(CancellationToken.None);

        var top = Assert.Single(data.TopProducts);
        Assert.Equal(2, top.UnitsSold);
        Assert.Equal(256m, top.Revenue);

        var topCategory = Assert.Single(data.TopCategories);
        Assert.Equal("Clothing", topCategory.CategoryName);

        var topBrand = Assert.Single(data.TopBrands);
        Assert.Equal("Everlane", topBrand.BrandName);

        Assert.Equal(14, data.SalesTrend.Count);
        var recent = Assert.Single(data.RecentOrders);
        Assert.Equal("ORD-TOP1", recent.PublicOrderNumber);
    }

    [Fact]
    public async Task GetDashboardAsync_IsCachedUntilInvalidated()
    {
        var context = CreateContext();
        var cache = CreateCache();
        var service = CreateService(context, cache);

        context.Orders.Add(CreateOrder("ORD-C1", 100m, DateTime.UtcNow));
        await context.SaveChangesAsync();

        var first = await service.GetDashboardAsync(CancellationToken.None);
        Assert.Equal(100m, first.Metrics.SalesToday);

        // Mutate the underlying data directly; the cached payload is still served.
        context.Orders.Add(CreateOrder("ORD-C2", 999m, DateTime.UtcNow));
        await context.SaveChangesAsync();

        var second = await service.GetDashboardAsync(CancellationToken.None);
        Assert.Equal(100m, second.Metrics.SalesToday);

        await service.InvalidateCacheAsync(CancellationToken.None);
        var fresh = await service.GetDashboardAsync(CancellationToken.None);
        Assert.Equal(1099m, fresh.Metrics.SalesToday);
    }
}
