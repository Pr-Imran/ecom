using System.Text.Json;
using FashionStore.Application.Common;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.DTOs.Reports;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Aggregated administration dashboard. Every metric is computed with
/// database-side aggregation over bounded projections; the dashboard never loads
/// the full order set into memory. Financial figures are gross sales from
/// non-cancelled, paid (or partially paid) orders. Day/month boundaries follow
/// the store's configured timezone. The payload is cached for a short lifetime
/// and can be invalidated after order/payment/return/refund/customer changes.
/// </summary>
public sealed class AdminDashboardService : IAdminDashboardService
{
    private static readonly PaymentStatus[] PaidStatuses = { PaymentStatus.Paid, PaymentStatus.PartiallyPaid };

    private static readonly ReturnStatus[] ActiveReturnStatuses =
    {
        ReturnStatus.Requested, ReturnStatus.UnderReview, ReturnStatus.Approved,
        ReturnStatus.AwaitingShipment, ReturnStatus.InTransit, ReturnStatus.Received,
        ReturnStatus.Inspected, ReturnStatus.RefundPending
    };

    private static readonly OrderStatus[] OpenOrderStatuses =
    {
        OrderStatus.Placed, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped
    };

    private readonly AppDbContext _context;
    private readonly IWebsiteSettingsService _settings;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AdminDashboardService> _logger;

    public AdminDashboardService(
        AppDbContext context,
        IWebsiteSettingsService settings,
        IDistributedCache cache,
        ILogger<AdminDashboardService> logger)
    {
        _context = context;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AdminDashboardDataDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.AdminDashboard, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                return JsonSerializer.Deserialize<AdminDashboardDataDto>(cached)!;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Stale dashboard cache could not be deserialized; rebuilding.");
            }
        }

        var snapshot = await BuildAsync(cancellationToken);

        await _cache.SetStringAsync(
            CacheKeys.AdminDashboard,
            JsonSerializer.Serialize(snapshot),
            GetCacheOptions(),
            cancellationToken);

        return snapshot;
    }

    public async Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(CacheKeys.AdminDashboard, cancellationToken);
    }

    private async Task<AdminDashboardDataDto> BuildAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetSettingsAsync(cancellationToken);
        var currency = settings.Commerce.CurrencyCode;
        var tz = ReportDateRangeHelper.ResolveTimeZone(settings.Commerce.Timezone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

        var todayStartUtc = ToUtcStartOfDay(nowLocal, tz);
        var todayEndUtc = ToUtcStartOfDay(nowLocal.AddDays(1), tz);
        var monthStartUtc = ToUtcStartOfDay(new DateTime(nowLocal.Year, nowLocal.Month, 1), tz);
        var monthEndUtc = ToUtcStartOfDay(new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMonths(1), tz);

        var todayOrders = _context.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= todayStartUtc && o.CreatedAtUtc < todayEndUtc);
        var monthOrders = _context.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= monthStartUtc && o.CreatedAtUtc < monthEndUtc);

        var salesToday = await RevenueOfAsync(todayOrders, cancellationToken);
        var salesThisMonth = await RevenueOfAsync(monthOrders, cancellationToken);
        var ordersToday = await todayOrders.CountAsync(cancellationToken);
        var paidOrdersThisMonth = await monthOrders
            .CountAsync(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus), cancellationToken);

        var pendingOrders = await _context.Orders.AsNoTracking()
            .CountAsync(o => OpenOrderStatuses.Contains(o.OrderStatus), cancellationToken);
        var paidOrders = await _context.Orders.AsNoTracking()
            .CountAsync(o => o.PaymentStatus == PaymentStatus.Paid, cancellationToken);
        var failedPayments = await _context.Payments.AsNoTracking()
            .CountAsync(p => p.State == PaymentState.Failed && p.CreatedAtUtc >= monthStartUtc && p.CreatedAtUtc < monthEndUtc, cancellationToken);

        var newCustomers = await _context.Users.AsNoTracking()
            .CountAsync(u => u.CreatedAtUtc >= monthStartUtc && u.CreatedAtUtc < monthEndUtc, cancellationToken);

        var (lowStock, outOfStock) = await GetStockCountsAsync(cancellationToken);

        var pendingReturns = await _context.ReturnRequests.AsNoTracking()
            .CountAsync(r => !r.IsWithdrawn && ActiveReturnStatuses.Contains(r.Status), cancellationToken);
        var pendingRefunds = await _context.Refunds.AsNoTracking()
            .CountAsync(r => r.Status == RefundStatus.Pending, cancellationToken);

        var averageOrderValue = paidOrdersThisMonth > 0 ? salesThisMonth / paidOrdersThisMonth : 0m;

        var topProducts = await GetTopProductsAsync(monthStartUtc, monthEndUtc, cancellationToken);
        var topCategories = await GetTopCategoriesAsync(monthStartUtc, monthEndUtc, cancellationToken);
        var topBrands = await GetTopBrandsAsync(monthStartUtc, monthEndUtc, cancellationToken);
        var recentOrders = await GetRecentOrdersAsync(cancellationToken);
        var salesTrend = await GetSalesTrendAsync(tz, nowLocal, cancellationToken);

        var metrics = new AdminDashboardMetricsDto(
            salesToday,
            salesThisMonth,
            ordersToday,
            pendingOrders,
            paidOrders,
            failedPayments,
            lowStock,
            outOfStock,
            pendingReturns,
            pendingRefunds,
            newCustomers,
            averageOrderValue,
            currency);

        return new AdminDashboardDataDto(
            metrics,
            topProducts,
            topCategories,
            topBrands,
            recentOrders,
            salesTrend,
            BuildAccuracyNotes(tz.Id, currency));
    }

    private static async Task<decimal> RevenueOfAsync(IQueryable<Order> orders, CancellationToken cancellationToken)
    {
        return await orders
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus))
            .SumAsync(o => (decimal?)o.GrandTotal, cancellationToken) ?? 0m;
    }

    private async Task<(int LowStock, int OutOfStock)> GetStockCountsAsync(CancellationToken cancellationToken)
    {
        var groups = await _context.WarehouseStocks.AsNoTracking()
            .GroupBy(s => s.ProductVariantId)
            .Select(g => new
            {
                Available = g.Sum(s => s.OnHandQuantity) - g.Sum(s => s.ReservedQuantity),
                MinThreshold = g.Min(s => s.LowStockThreshold)
            })
            .ToListAsync(cancellationToken);

        var lowStock = groups.Count(g => g.MinThreshold.HasValue && g.Available <= g.MinThreshold.Value);
        var outOfStock = groups.Count(g => g.Available <= 0);
        return (lowStock, outOfStock);
    }

    private async Task<IReadOnlyList<AdminTopProductDto>> GetTopProductsAsync(
        DateTime monthStartUtc,
        DateTime monthEndUtc,
        CancellationToken cancellationToken)
    {
        return await _context.OrderItems.AsNoTracking()
            .Where(i => i.Order!.CreatedAtUtc >= monthStartUtc &&
                        i.Order.CreatedAtUtc < monthEndUtc &&
                        i.Order.OrderStatus != OrderStatus.Cancelled)
            .GroupBy(i => new { i.ProductId, i.ProductName, i.Sku })
            .Select(g => new AdminTopProductDto(
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.Sku,
                g.Sum(x => x.Quantity),
                g.Sum(x => x.LineTotal)))
            .OrderByDescending(x => x.UnitsSold)
            .ThenByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AdminTopCategoryDto>> GetTopCategoriesAsync(
        DateTime monthStartUtc,
        DateTime monthEndUtc,
        CancellationToken cancellationToken)
    {
        var aggregate = await _context.OrderItems.AsNoTracking()
            .Where(i => i.ProductId != null &&
                        i.Order!.CreatedAtUtc >= monthStartUtc &&
                        i.Order.CreatedAtUtc < monthEndUtc &&
                        i.Order.OrderStatus != OrderStatus.Cancelled)
            .Join(_context.Products.AsNoTracking(), i => i.ProductId, p => p.Id, (i, p) => new { p.CategoryId, i.Quantity, i.LineTotal })
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, Units = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Units)
            .ThenByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync(cancellationToken);

        var categoryIds = aggregate.Select(x => x.CategoryId).ToList();
        var names = await _context.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return aggregate
            .Select(x => new AdminTopCategoryDto(x.CategoryId, names.TryGetValue(x.CategoryId, out var name) ? name : "Uncategorised", x.Units, x.Revenue))
            .ToList();
    }

    private async Task<IReadOnlyList<AdminTopBrandDto>> GetTopBrandsAsync(
        DateTime monthStartUtc,
        DateTime monthEndUtc,
        CancellationToken cancellationToken)
    {
        var aggregate = await _context.OrderItems.AsNoTracking()
            .Where(i => i.ProductId != null &&
                        i.Order!.CreatedAtUtc >= monthStartUtc &&
                        i.Order.CreatedAtUtc < monthEndUtc &&
                        i.Order.OrderStatus != OrderStatus.Cancelled)
            .Join(_context.Products.AsNoTracking(), i => i.ProductId, p => p.Id, (i, p) => new { p.BrandId, i.Quantity, i.LineTotal })
            .GroupBy(x => x.BrandId)
            .Select(g => new { BrandId = g.Key, Units = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Units)
            .ThenByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync(cancellationToken);

        var brandIds = aggregate.Where(x => x.BrandId.HasValue).Select(x => x.BrandId!.Value).ToList();
        var names = await _context.Brands.AsNoTracking()
            .Where(b => brandIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return aggregate
            .Select(x => new AdminTopBrandDto(
                x.BrandId,
                x.BrandId.HasValue && names.TryGetValue(x.BrandId.Value, out var name) ? name : "Unbranded",
                x.Units,
                x.Revenue))
            .ToList();
    }

    private async Task<IReadOnlyList<AdminOrderListItemDto>> GetRecentOrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await _context.Orders.AsNoTracking()
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(8)
            .ToListAsync(cancellationToken);

        return orders
            .Select(o => new AdminOrderListItemDto(
                o.Id,
                o.PublicOrderNumber,
                o.InvoiceNumber,
                string.IsNullOrEmpty(o.UserId),
                o.CustomerName,
                o.GuestEmail,
                o.GuestPhone,
                o.Currency,
                o.GrandTotal,
                o.OrderStatus.ToString(),
                o.PaymentStatus.ToString(),
                o.FulfilmentStatus.ToString(),
                o.PaymentMethodCode,
                o.ShippingMethodName,
                o.Items.Sum(i => i.Quantity),
                o.CreatedAtUtc,
                o.PaidAtUtc,
                o.ShippedAtUtc,
                o.DeliveredAtUtc))
            .ToList();
    }

    private async Task<IReadOnlyList<AdminSalesTrendPointDto>> GetSalesTrendAsync(
        TimeZoneInfo tz,
        DateTime nowLocal,
        CancellationToken cancellationToken)
    {
        const int trendDays = 14;
        var trendStartUtc = ToUtcStartOfDay(nowLocal.AddDays(-(trendDays - 1)), tz);
        var trendEndUtc = ToUtcStartOfDay(nowLocal.AddDays(1), tz);

        var rows = await _context.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= trendStartUtc &&
                        o.CreatedAtUtc < trendEndUtc &&
                        o.OrderStatus != OrderStatus.Cancelled &&
                        PaidStatuses.Contains(o.PaymentStatus))
            .Select(o => new { o.CreatedAtUtc, o.GrandTotal })
            .ToListAsync(cancellationToken);

        var points = new List<AdminSalesTrendPointDto>(trendDays);
        for (var day = 0; day < trendDays; day++)
        {
            var dayUtc = ToUtcStartOfDay(nowLocal.AddDays(-(trendDays - 1) + day), tz);
            var nextDayUtc = dayUtc.AddDays(1);
            var dayRows = rows.Where(r => r.CreatedAtUtc >= dayUtc && r.CreatedAtUtc < nextDayUtc).ToList();
            points.Add(new AdminSalesTrendPointDto(
                dayUtc,
                dayRows.Count,
                dayRows.Sum(r => r.GrandTotal)));
        }

        return points;
    }

    private static DateTime ToUtcStartOfDay(DateTime localDate, TimeZoneInfo tz)
    {
        var unspecified = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }

    private static List<string> BuildAccuracyNotes(string timezoneId, string currency)
    {
        return new List<string>
        {
            "Sales figures are GROSS sales: the sum of order GrandTotal for non-cancelled orders whose payment status is Paid or PartiallyPaid.",
            "Cancelled orders are excluded from all sales, revenue and average-order-value figures.",
            "Failed-payment orders are excluded from sales (they appear under failed payments).",
            "Refunded and partially refunded orders REMAIN included in gross sales; refund money is tracked separately in the refund report.",
            "Sales today / this month and orders today use the store timezone (" + timezoneId + ").",
            "Orders today counts every order placed today regardless of status, including cancelled orders.",
            "Pending orders are orders in the placed/confirmed/processing/shipped states.",
            "Paid orders is the all-time count of orders with payment status Paid.",
            "Failed payments counts payment records that reached a Failed state during this month.",
            "Low-stock and out-of-stock variants are current stock snapshots aggregated across warehouses (available = on-hand - reserved; low = at or below the lowest low-stock threshold).",
            "Pending returns are returns in an active, non-terminal state (not rejected, refunded, exchanged or closed) and not withdrawn.",
            "Pending refunds are refund records with status Pending.",
            "New customers counts user accounts created during this month.",
            "Average order value = this month's gross sales / this month's paid order count.",
            "Sales trend covers the last 14 local days and includes only non-cancelled paid orders (gross sales).",
            "Amounts are shown in the store currency (" + currency + ")."
        };
    }

    private DistributedCacheEntryOptions GetCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
    };
}
