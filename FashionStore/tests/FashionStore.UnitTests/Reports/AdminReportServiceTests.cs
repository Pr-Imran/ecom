using System.Linq.Expressions;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Reports;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.BackgroundJobs;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionStore.UnitTests.Reports;

public class AdminReportServiceTests
{
    private static AppDbContext CreateContext() => SqliteTestContext.Create();

    private static MemoryDistributedCache CreateCache() =>
        new(new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static AdminReportService CreateService(
        AppDbContext context,
        IDistributedCache? cache = null,
        Mock<IBackgroundJobClient>? backgroundJobs = null)
        => new(
            context,
            cache ?? CreateCache(),
            (backgroundJobs ?? new Mock<IBackgroundJobClient>()).Object,
            Options.Create(new AdminReportSettings()),
            NullLogger<AdminReportService>.Instance);

    private static Order CreateOrder(
        string number,
        decimal grandTotal,
        DateTime createdAtUtc,
        PaymentStatus paymentStatus = PaymentStatus.Paid,
        OrderStatus orderStatus = OrderStatus.Placed,
        decimal refundedAmount = 0m)
        => new()
        {
            PublicOrderNumber = number,
            Currency = "USD",
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

    private static AdminReportRequest Range(DateTime from, DateTime to, int page = 1, int pageSize = 20) =>
        new(from, to, null, null, null, null, null, null, null, null, page, pageSize);

    [Fact]
    public async Task SalesReport_IncludesPaidExcludesCancelledAndUnpaid_AppliesRefundAdjustments()
    {
        var context = CreateContext();
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        context.Orders.AddRange(
            CreateOrder("ORD-1", 100m, from.AddDays(1)),
            CreateOrder("ORD-2", 50m, from.AddDays(1), orderStatus: OrderStatus.Cancelled),
            CreateOrder("ORD-3", 25m, from.AddDays(1), paymentStatus: PaymentStatus.Unpaid),
            CreateOrder("ORD-4", 200m, from.AddDays(1), refundedAmount: 40m),
            CreateOrder("ORD-5", 60m, to.AddDays(5)));
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetSalesReportAsync(Range(from, to), CancellationToken.None);

        // ORD-1 + ORD-4 gross; cancelled/unpaid/out-of-range excluded.
        Assert.Equal(2, result.Totals.OrderCount);
        Assert.Equal(300m, result.Totals.GrossSales);
        Assert.Equal(40m, result.Totals.Refunds);
        Assert.Equal(260m, result.Totals.NetSales);
        Assert.Single(result.Items);
        Assert.Contains("cancelled", result.AccuracyNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesReport_RespectsDateRangeBoundaries()
    {
        var context = CreateContext();
        var from = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);

        context.Orders.AddRange(
            CreateOrder("ORD-A", 10m, new DateTime(2026, 8, 9, 23, 59, 59, DateTimeKind.Utc)),
            CreateOrder("ORD-B", 20m, new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc)),
            CreateOrder("ORD-C", 30m, new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc)),
            CreateOrder("ORD-D", 40m, new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc)));
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetSalesReportAsync(Range(from, to), CancellationToken.None);

        // ORD-B and ORD-C fall in [from, to); ORD-A before, ORD-D at the exclusive upper bound.
        Assert.Equal(2, result.Totals.OrderCount);
        Assert.Equal(50m, result.Totals.GrossSales);
    }

    [Fact]
    public async Task SalesReport_IsPaginated()
    {
        var context = CreateContext();
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(3);

        context.Orders.AddRange(
            CreateOrder("ORD-1", 10m, from),
            CreateOrder("ORD-2", 20m, from.AddDays(1)),
            CreateOrder("ORD-3", 30m, from.AddDays(2)));
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var page1 = await service.GetSalesReportAsync(Range(from, to, page: 1, pageSize: 2), CancellationToken.None);
        var page2 = await service.GetSalesReportAsync(Range(from, to, page: 2, pageSize: 2), CancellationToken.None);

        Assert.Equal(3, page1.Paging.TotalCount);
        Assert.True(page1.Paging.HasMore);
        Assert.Equal(2, page1.Items.Count);
        Assert.False(page2.Paging.HasMore);
        Assert.Single(page2.Items);
        // Totals are unaffected by paging.
        Assert.Equal(60m, page1.Totals.GrossSales);
    }

    [Fact]
    public async Task OrderReport_ListsEveryStatus_AndSupportsStatusFilter()
    {
        var context = CreateContext();
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        context.Orders.AddRange(
            CreateOrder("ORD-1", 100m, from.AddDays(1)),
            CreateOrder("ORD-2", 50m, from.AddDays(2), orderStatus: OrderStatus.Cancelled),
            CreateOrder("ORD-3", 25m, from.AddDays(3), paymentStatus: PaymentStatus.Unpaid));
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var all = await service.GetOrderReportAsync(Range(from, to), CancellationToken.None);
        Assert.Equal(3, all.Paging.TotalCount);
        Assert.Contains(all.Items, r => r.OrderStatus == "Cancelled");

        var cancelled = await service.GetOrderReportAsync(Range(from, to) with { Status = "Cancelled" }, CancellationToken.None);
        Assert.Single(cancelled.Items);
        Assert.Equal("ORD-2", cancelled.Items[0].OrderNumber);
    }

    [Fact]
    public async Task CouponUsageReport_AggregatesNonVoidedUsage()
    {
        var context = CreateContext();
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        var coupon = new Coupon
        {
            Code = "SAVE10",
            NormalizedCode = "SAVE10",
            Name = "Save ten",
            DiscountValue = 10m,
            IsActive = true,
            CreatedAtUtc = from,
            UpdatedAtUtc = from
        };
        context.Coupons.Add(coupon);
        context.CouponUsages.AddRange(
            new CouponUsage { CouponId = coupon.Id, UserId = "u1", AmountDiscounted = 10m, UsedAtUtc = from.AddDays(1) },
            new CouponUsage { CouponId = coupon.Id, UserId = "u1", AmountDiscounted = 10m, UsedAtUtc = from.AddDays(2) },
            new CouponUsage { CouponId = coupon.Id, UserId = "u2", AmountDiscounted = 20m, UsedAtUtc = from.AddDays(3) },
            new CouponUsage { CouponId = coupon.Id, UserId = "u3", AmountDiscounted = 99m, UsedAtUtc = from.AddDays(4), VoidedAtUtc = from.AddDays(5) },
            new CouponUsage { CouponId = coupon.Id, UserId = "u4", AmountDiscounted = 99m, UsedAtUtc = to.AddDays(1) });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetCouponUsageReportAsync(Range(from, to), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(3, row.UsageCount);
        Assert.Equal(2, row.DistinctCustomers);
        Assert.Equal(40m, row.TotalDiscounted);
        Assert.Equal(40m, result.TotalDiscounted);
    }

    [Fact]
    public async Task InventoryReport_AggregatesWarehouseStockAcrossVariants()
    {
        var context = CreateContext();
        var category = new Category { Name = "Clothing", Slug = "clothing", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        var product = new Product
        {
            Name = "Cashmere Crew Neck Sweater",
            Slug = "cashmere-crew-neck-sweater",
            BaseSku = "SW-1001",
            BasePrice = 128m,
            Category = category,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(product);

        var variant = new ProductVariant
        {
            Product = product,
            Sku = "SW-1001-GREY-M",
            Price = 128m,
            IsActive = true
        };
        context.ProductVariants.Add(variant);

        var w1 = Guid.NewGuid();
        var w2 = Guid.NewGuid();
        context.WarehouseStocks.AddRange(
            new WarehouseStock { WarehouseId = w1, ProductVariantId = variant.Id, OnHandQuantity = 10, ReservedQuantity = 2, LowStockThreshold = 5 },
            new WarehouseStock { WarehouseId = w2, ProductVariantId = variant.Id, OnHandQuantity = 4, ReservedQuantity = 0, LowStockThreshold = 5 });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetInventoryReportAsync(Range(DateTime.MinValue, DateTime.MaxValue), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(14, row.TotalOnHand);
        Assert.Equal(2, row.TotalReserved);
        Assert.Equal(12, row.TotalAvailable);
        Assert.Equal(5, row.LowStockThreshold);
        Assert.Equal(1, result.TotalVariants);
        Assert.Equal(14, result.TotalUnits);
    }

    [Fact]
    public async Task RequestExportAsync_EnqueuesBackgroundJob_AndReturnsPreparing()
    {
        var context = CreateContext();
        var jobs = new Mock<IBackgroundJobClient>();
        var service = CreateService(context, backgroundJobs: jobs);

        var request = Range(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(1));
        var result = await service.RequestExportAsync(ReportTypes.Sales, request, CancellationToken.None);

        Assert.Equal(AdminReportExportStatus.Preparing, result.Status);
        Assert.Equal(ReportTypes.Sales, result.ReportType);
        Assert.NotNull(result.JobId);
        jobs.Verify(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task RequestExportAsync_UnknownReportType_ReturnsFailed()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.RequestExportAsync("does-not-exist", Range(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow), CancellationToken.None);

        Assert.Equal(AdminReportExportStatus.Failed, result.Status);
        Assert.Contains("Unknown", result.ErrorMessage);
    }

    [Fact]
    public async Task BuildExportFileAsync_WritesCsvAndMarksReady()
    {
        var context = CreateContext();
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);
        context.Orders.Add(CreateOrder("ORD-1", 100m, from.AddDays(1)));
        await context.SaveChangesAsync();

        var cache = CreateCache();
        var service = CreateService(context, cache);

        var jobId = Guid.NewGuid().ToString("N");
        var contentRoot = Path.Combine(Path.GetTempPath(), "fs-exports-" + Guid.NewGuid().ToString("N"));
        var request = Range(from, to);

        await service.BuildExportFileAsync(jobId, ReportTypes.Sales, request, contentRoot, CancellationToken.None);

        var status = await service.GetExportStatusAsync(jobId, CancellationToken.None);
        Assert.Equal(AdminReportExportStatus.Ready, status.Status);
        Assert.Equal(jobId + ".csv", status.FileName);

        var path = await service.GetExportFilePathAsync(jobId, CancellationToken.None);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("DayUtc,OrderCount,GrossSales", text);
        Assert.Contains("100", text);
    }

    [Fact]
    public async Task FilterOptions_AreCachedUntilInvalidated()
    {
        var context = CreateContext();
        var cache = CreateCache();
        var service = CreateService(context, cache);

        context.Categories.Add(new Category { Name = "Clothing", Slug = "clothing", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var first = await service.GetFilterOptionsAsync(CancellationToken.None);
        Assert.Single(first.Categories);

        context.Categories.Add(new Category { Name = "Footwear", Slug = "footwear", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var cached = await service.GetFilterOptionsAsync(CancellationToken.None);
        Assert.Single(cached.Categories);

        await service.InvalidateCacheAsync(CancellationToken.None);
        var fresh = await service.GetFilterOptionsAsync(CancellationToken.None);
        Assert.Equal(2, fresh.Categories.Count);
    }
}
