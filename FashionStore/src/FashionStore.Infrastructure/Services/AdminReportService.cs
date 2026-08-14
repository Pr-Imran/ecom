using System.Globalization;
using System.Text;
using System.Text.Json;
using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Reports;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.BackgroundJobs;
using FashionStore.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Aggregated administrative reports. Every report uses database-side grouping
/// and projection, is always constrained to a bounded date range, is paginated
/// and cached, and never materialises the full order set. Exports are prepared
/// in the background by paging over the same queries and streaming rows to a CSV
/// file so memory stays bounded regardless of result size.
/// </summary>
public sealed class AdminReportService : IAdminReportService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private static readonly PaymentStatus[] PaidStatuses = { PaymentStatus.Paid, PaymentStatus.PartiallyPaid };

    private const string SalesAccuracyNote =
        "Gross sales = the sum of GrandTotal for non-cancelled orders whose payment status is Paid or PartiallyPaid. " +
        "Cancelled and failed-payment orders are excluded. Refunded and partially refunded orders remain included in gross " +
        "sales; refunded amounts are subtracted to produce net sales. Amounts are grouped by order date in UTC.";

    private const string OrderReportAccuracyNote =
        "Order report lists every order in the date range regardless of status, including cancelled, failed-payment, " +
        "refunded and partially refunded orders. The status filter narrows by order status.";

    private const string ProductVolumeAccuracyNote =
        "Based on line items of non-cancelled orders whose payment status is Paid or PartiallyPaid. Cancelled, unpaid and " +
        "failed-payment orders are excluded; refunded and partially refunded orders remain included (gross revenue).";

    private const string CustomerAccuracyNote =
        "Total spent = sum of GrandTotal for non-cancelled paid (or partially paid) orders in the range. Total refunded is the " +
        "order-level refunded amount for those orders. Cancelled, unpaid and failed-payment orders are excluded.";

    private const string CouponAccuracyNote =
        "Usage counts include only non-voided coupon usages in the date range, grouped by coupon.";

    private const string InventoryAccuracyNote =
        "Inventory report is a live stock snapshot (not order data). It covers variants that have warehouse stock rows; " +
        "available = total on-hand minus total reserved. Low stock is at or below the lowest low-stock threshold; out of stock is " +
        "available <= 0.";

    private const string ReturnAccuracyNote =
        "Return report lists return requests created in the date range regardless of status, including rejected, refunded, " +
        "exchanged and closed returns.";

    private const string RefundAccuracyNote =
        "Refund report lists refund records created in the date range regardless of status (pending, succeeded, failed or voided).";

    private const string PaymentAccuracyNote =
        "Payment report lists payment records created in the date range regardless of state, including failed, cancelled and " +
        "expired payments.";

    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IOptions<AdminReportSettings> _settings;
    private readonly ILogger<AdminReportService> _logger;

    public AdminReportService(
        AppDbContext context,
        IDistributedCache cache,
        IBackgroundJobClient backgroundJobs,
        IOptions<AdminReportSettings> settings,
        ILogger<AdminReportService> logger)
    {
        _context = context;
        _cache = cache;
        _backgroundJobs = backgroundJobs;
        _settings = settings;
        _logger = logger;
    }

    // ---- Filter options ----

    public async Task<AdminReportFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.AdminReportFilters, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                return JsonSerializer.Deserialize<AdminReportFilterOptions>(cached)!;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Stale report filter cache could not be deserialized; rebuilding.");
            }
        }

        var categories = await _context.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new AdminReportOptionDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);

        var brands = await _context.Brands.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new AdminReportOptionDto(b.Id, b.Name))
            .ToListAsync(cancellationToken);

        var products = await _context.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Take(1000)
            .Select(p => new AdminReportOptionDto(p.Id, p.Name))
            .ToListAsync(cancellationToken);

        var paymentMethods = await _context.Orders.AsNoTracking()
            .Where(o => o.PaymentMethodCode != null)
            .Select(o => o.PaymentMethodCode!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        var shippingMethods = await _context.ShippingMethods.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new AdminReportOptionDto(m.Id, m.Name))
            .ToListAsync(cancellationToken);

        var customerRows = await _context.Orders.AsNoTracking()
            .Where(o => o.UserId != null || o.GuestEmail != null)
            .Select(o => new { o.UserId, o.GuestEmail, o.CustomerName })
            .Take(1000)
            .ToListAsync(cancellationToken);

        var customers = customerRows
            .GroupBy(c => c.UserId ?? c.GuestEmail!)
            .Select(g => new AdminReportCustomerOptionDto(
                g.Key,
                g.Select(x => x.CustomerName).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? g.Key))
            .OrderBy(c => c.Name)
            .ToList();

        var currencies = await _context.Orders.AsNoTracking()
            .Select(o => o.Currency)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        var options = new AdminReportFilterOptions(
            categories, brands, products, paymentMethods, shippingMethods, customers, currencies);

        await _cache.SetStringAsync(
            CacheKeys.AdminReportFilters,
            JsonSerializer.Serialize(options),
            GetCacheOptions(30),
            cancellationToken);

        return options;
    }

    // ---- Sales report ----

    public async Task<AdminSalesReportResult> GetSalesReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Sales, request);
        var cached = await GetCachedAsync<AdminSalesReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus));

        var grouped = orders
            .GroupBy(o => o.CreatedAtUtc.Date)
            .Select(g => new
            {
                Day = g.Key,
                OrderCount = g.Count(),
                Gross = g.Sum(x => x.GrandTotal),
                Discount = g.Sum(x => x.ProductDiscount + x.CouponDiscount),
                Shipping = g.Sum(x => x.ShippingCharge),
                Tax = g.Sum(x => x.Tax),
                Refunded = g.Sum(x => x.RefundedAmount)
            });

        var totalDays = await grouped.CountAsync(cancellationToken);

        var items = await grouped
            .OrderByDescending(x => x.Day)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminSalesReportRowDto(
                x.Day,
                x.OrderCount,
                x.Gross,
                x.Discount,
                x.Shipping,
                x.Tax,
                x.Refunded,
                x.Gross - x.Refunded))
            .ToListAsync(cancellationToken);

        var totals = await orders
            .GroupBy(o => 1)
            .Select(g => new AdminSalesReportTotalsDto(
                g.Count(),
                g.Sum(x => x.GrandTotal),
                g.Sum(x => x.ProductDiscount + x.CouponDiscount),
                g.Sum(x => x.ShippingCharge),
                g.Sum(x => x.Tax),
                g.Sum(x => x.RefundedAmount),
                g.Sum(x => x.GrandTotal) - g.Sum(x => x.RefundedAmount)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new AdminSalesReportTotalsDto(0, 0m, 0m, 0m, 0m, 0m, 0m);

        var result = new AdminSalesReportResult(
            items,
            new AdminReportPagingDto(totalDays, page, pageSize, page * pageSize < totalDays),
            totals,
            SalesAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Order report ----

    public async Task<AdminOrderReportResult> GetOrderReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Orders, request);
        var cached = await GetCachedAsync<AdminOrderReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to);

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var orderStatus))
        {
            orders = orders.Where(o => o.OrderStatus == orderStatus);
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var totalGrandTotal = await orders.SumAsync(o => (decimal?)o.GrandTotal, cancellationToken) ?? 0m;

        var items = await orders
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderReportRowDto(
                o.Id,
                o.PublicOrderNumber,
                o.CustomerName,
                o.GuestEmail,
                string.IsNullOrEmpty(o.UserId),
                o.Currency,
                o.GrandTotal,
                o.PaidAmount,
                o.RefundedAmount,
                o.GrandTotal - o.PaidAmount,
                o.OrderStatus.ToString(),
                o.PaymentStatus.ToString(),
                o.PaymentMethodCode,
                o.ShippingMethodName,
                o.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var result = new AdminOrderReportResult(
            items,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalGrandTotal,
            OrderReportAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Product sales report ----

    public async Task<AdminProductSalesReportResult> GetProductSalesReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.ProductSales, request);
        var cached = await GetCachedAsync<AdminProductSalesReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (page, pageSize) = NormalizePage(request);
        var itemsQuery = BuildItemReportQuery(request);

        var totalCount = await itemsQuery.CountAsync(cancellationToken);
        var totals = await itemsQuery
            .GroupBy(x => 1)
            .Select(g => new { Revenue = g.Sum(x => (decimal?)x.Revenue), Units = g.Sum(x => (int?)x.Units) })
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await itemsQuery
            .OrderByDescending(x => x.Revenue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminProductSalesReportRowDto(
                x.ProductId,
                x.ProductName,
                x.Sku,
                x.CategoryName,
                x.BrandName,
                x.Units,
                x.OrderCount,
                x.Revenue))
            .ToListAsync(cancellationToken);

        var result = new AdminProductSalesReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totals?.Revenue ?? 0m,
            totals?.Units ?? 0,
            ProductVolumeAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Variation sales report ----

    public async Task<AdminVariationSalesReportResult> GetVariationSalesReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.VariationSales, request);
        var cached = await GetCachedAsync<AdminVariationSalesReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus));

        var aggregate = await orders
            .SelectMany(o => o.Items.Where(i => i.ProductVariantId != null), (o, i) => new { i.ProductVariantId, i.ProductName, i.Sku, i.Quantity, i.LineTotal, i.OrderId })
            .GroupBy(x => new { x.ProductVariantId, x.ProductName, x.Sku })
            .Select(g => new
            {
                g.Key.ProductVariantId,
                g.Key.ProductName,
                g.Key.Sku,
                Units = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal),
                OrderCount = g.Select(x => x.OrderId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var totalCount = aggregate.Count;
        var totalRevenue = aggregate.Sum(x => x.Revenue);
        var totalUnits = aggregate.Sum(x => x.Units);

        var variantIds = aggregate.Select(x => x.ProductVariantId!.Value).ToList();
        var variantNames = await LoadVariantNamesAsync(variantIds, cancellationToken);

        var rows = aggregate
            .OrderByDescending(x => x.Revenue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminVariationSalesReportRowDto(
                x.ProductVariantId!.Value,
                x.Sku,
                x.ProductName,
                variantNames.TryGetValue(x.ProductVariantId!.Value, out var name) ? name : null,
                x.Units,
                x.OrderCount,
                x.Revenue))
            .ToList();

        var result = new AdminVariationSalesReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalRevenue,
            totalUnits,
            ProductVolumeAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Category report ----

    public async Task<AdminCategoryReportResult> GetCategoryReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Category, request);
        var cached = await GetCachedAsync<AdminCategoryReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (page, pageSize) = NormalizePage(request);

        var aggregate = await BuildItemReportQuery(request)
            .Select(x => new { x.CategoryId, x.Units, x.Revenue, x.OrderCount })
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, Units = g.Sum(x => x.Units), Revenue = g.Sum(x => x.Revenue), OrderCount = g.Sum(x => x.OrderCount) })
            .ToListAsync(cancellationToken);

        var categoryIds = aggregate.Select(x => x.CategoryId).ToList();
        var names = await _context.Categories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var totalCount = aggregate.Count;
        var totalRevenue = aggregate.Sum(x => x.Revenue);
        var totalUnits = aggregate.Sum(x => x.Units);

        var rows = aggregate
            .OrderByDescending(x => x.Revenue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminCategoryReportRowDto(
                x.CategoryId,
                names.TryGetValue(x.CategoryId, out var name) ? name : "Uncategorised",
                x.Units,
                x.OrderCount,
                x.Revenue,
                totalRevenue > 0 ? Math.Round(x.Revenue / totalRevenue * 100m, 2) : 0m))
            .ToList();

        var result = new AdminCategoryReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalRevenue,
            totalUnits,
            ProductVolumeAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Brand report ----

    public async Task<AdminBrandReportResult> GetBrandReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Brand, request);
        var cached = await GetCachedAsync<AdminBrandReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (page, pageSize) = NormalizePage(request);

        var aggregate = await BuildItemReportQuery(request)
            .Select(x => new { x.BrandId, x.Units, x.Revenue, x.OrderCount })
            .GroupBy(x => x.BrandId)
            .Select(g => new { BrandId = g.Key, Units = g.Sum(x => x.Units), Revenue = g.Sum(x => x.Revenue), OrderCount = g.Sum(x => x.OrderCount) })
            .ToListAsync(cancellationToken);

        var brandIds = aggregate.Where(x => x.BrandId.HasValue).Select(x => x.BrandId!.Value).ToList();
        var names = await _context.Brands.AsNoTracking()
            .Where(b => brandIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var totalCount = aggregate.Count;
        var totalRevenue = aggregate.Sum(x => x.Revenue);
        var totalUnits = aggregate.Sum(x => x.Units);

        var rows = aggregate
            .OrderByDescending(x => x.Revenue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminBrandReportRowDto(
                x.BrandId,
                x.BrandId.HasValue && names.TryGetValue(x.BrandId.Value, out var name) ? name : "Unbranded",
                x.Units,
                x.OrderCount,
                x.Revenue,
                totalRevenue > 0 ? Math.Round(x.Revenue / totalRevenue * 100m, 2) : 0m))
            .ToList();

        var result = new AdminBrandReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalRevenue,
            totalUnits,
            ProductVolumeAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Customer report ----

    public async Task<AdminCustomerReportResult> GetCustomerReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Customer, request);
        var cached = await GetCachedAsync<AdminCustomerReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus))
            .Where(o => o.UserId != null || o.GuestEmail != null);

        var aggregate = await orders
            .GroupBy(o => new { o.UserId, o.GuestEmail })
            .Select(g => new
            {
                UserId = g.Key.UserId,
                GuestEmail = g.Key.GuestEmail,
                OrderCount = g.Count(),
                TotalSpent = g.Sum(x => x.GrandTotal),
                TotalRefunded = g.Sum(x => x.RefundedAmount),
                LastOrderAtUtc = g.Max(x => x.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        var userNames = await _context.Users.AsNoTracking()
            .Where(u => aggregate.Any(x => x.UserId == u.Id))
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.DisplayName })
            .ToListAsync(cancellationToken);
        var nameByUserId = userNames.ToDictionary(u => u.Id, u => u.DisplayName ?? $"{u.FirstName} {u.LastName}".Trim());
        var emailByUserId = userNames.ToDictionary(u => u.Id, u => u.Email);

        var totalCount = aggregate.Count;
        var totalSpent = aggregate.Sum(x => x.TotalSpent);

        var rows = aggregate
            .OrderByDescending(x => x.TotalSpent)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x =>
            {
                var email = x.UserId != null && emailByUserId.TryGetValue(x.UserId, out var e) ? e : x.GuestEmail;
                var name = x.UserId != null && nameByUserId.TryGetValue(x.UserId, out var n) ? n : x.GuestEmail;
                return new AdminCustomerReportRowDto(
                    x.UserId ?? x.GuestEmail,
                    string.IsNullOrWhiteSpace(name) ? email : name,
                    email,
                    x.OrderCount,
                    x.TotalSpent,
                    x.TotalRefunded,
                    x.LastOrderAtUtc);
            })
            .ToList();

        var result = new AdminCustomerReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalSpent,
            CustomerAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Coupon usage report ----

    public async Task<AdminCouponUsageReportResult> GetCouponUsageReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.CouponUsage, request);
        var cached = await GetCachedAsync<AdminCouponUsageReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var usages = _context.CouponUsages.AsNoTracking()
            .Where(u => u.VoidedAtUtc == null && u.UsedAtUtc >= from && u.UsedAtUtc < to);

        var aggregate = await usages
            .GroupBy(u => u.CouponId)
            .Select(g => new
            {
                CouponId = g.Key,
                UsageCount = g.Count(),
                DistinctCustomers = g.Select(x => x.UserId).Distinct().Count(),
                TotalDiscounted = g.Sum(x => x.AmountDiscounted)
            })
            .ToListAsync(cancellationToken);

        var couponIds = aggregate.Select(x => x.CouponId).ToList();
        var coupons = await _context.Coupons.AsNoTracking()
            .Where(c => couponIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c, cancellationToken);

        var totalCount = aggregate.Count;
        var totalDiscounted = aggregate.Sum(x => x.TotalDiscounted);

        var rows = aggregate
            .OrderByDescending(x => x.TotalDiscounted)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => coupons.TryGetValue(x.CouponId, out var c)
                ? new AdminCouponUsageReportRowDto(x.CouponId, c.Code, c.Name, x.UsageCount, x.DistinctCustomers, x.TotalDiscounted)
                : new AdminCouponUsageReportRowDto(x.CouponId, "Deleted", "Deleted coupon", x.UsageCount, x.DistinctCustomers, x.TotalDiscounted))
            .ToList();

        var result = new AdminCouponUsageReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalDiscounted,
            CouponAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Inventory report ----

    public async Task<AdminInventoryReportResult> GetInventoryReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Inventory, request);
        var cached = await GetCachedAsync<AdminInventoryReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (page, pageSize) = NormalizePage(request);

        var all = await BuildInventoryRowsAsync(cancellationToken);
        var totalVariants = all.Count;
        var totalUnits = all.Sum(x => x.TotalOnHand);

        var filtered = all
            .Where(x => !request.CategoryId.HasValue || x.CategoryId == request.CategoryId.Value)
            .Where(x => !request.BrandId.HasValue || x.BrandId == request.BrandId.Value)
            .Where(x => !request.ProductId.HasValue || x.ProductId == request.ProductId.Value)
            .Where(x => string.IsNullOrWhiteSpace(request.Status) ||
                        string.Equals(x.StockStatus, request.Status.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rows = filtered
            .OrderBy(x => x.Sku)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminInventoryReportRowDto(
                x.VariantId,
                x.Sku,
                x.ProductName,
                x.CategoryName,
                x.TotalOnHand,
                x.TotalReserved,
                x.TotalAvailable,
                x.LowStockThreshold,
                x.AllowBackorder,
                x.StockStatus))
            .ToList();

        var result = new AdminInventoryReportResult(
            rows,
            new AdminReportPagingDto(filtered.Count, page, pageSize, page * pageSize < filtered.Count),
            totalVariants,
            totalUnits,
            InventoryAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Return report ----

    public async Task<AdminReturnReportResult> GetReturnReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Returns, request);
        var cached = await GetCachedAsync<AdminReturnReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var query = _context.ReturnRequests.AsNoTracking()
            .Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to);

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<ReturnStatus>(request.Status, ignoreCase: true, out var returnStatus))
        {
            query = query.Where(r => r.Status == returnStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            query = query.Where(r => r.Currency == request.Currency);
        }

        if (!string.IsNullOrWhiteSpace(request.Customer))
        {
            var term = request.Customer.Trim();
            query = query.Where(r =>
                (r.CustomerName != null && r.CustomerName.Contains(term)) ||
                (r.GuestEmail != null && r.GuestEmail.Contains(term)) ||
                r.UserId == term);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totals = await query
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Refundable = g.Sum(x => (decimal?)x.RefundableAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminReturnReportRowDto(
                r.Id,
                r.ReturnNumber,
                r.Order != null ? r.Order.PublicOrderNumber : null,
                r.CustomerName,
                r.GuestEmail,
                r.ReasonCode.ToString(),
                r.Status.ToString(),
                r.RefundableAmount,
                r.RefundedAmount,
                r.IsExchange,
                r.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var result = new AdminReturnReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totals?.Count ?? 0,
            totals?.Refundable ?? 0m,
            ReturnAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Refund report ----

    public async Task<AdminRefundReportResult> GetRefundReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Refunds, request);
        var cached = await GetCachedAsync<AdminRefundReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var query = _context.Refunds.AsNoTracking()
            .Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to);

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<RefundStatus>(request.Status, ignoreCase: true, out var refundStatus))
        {
            query = query.Where(r => r.Status == refundStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            query = query.Where(r => r.Currency == request.Currency);
        }

        if (!string.IsNullOrWhiteSpace(request.Customer))
        {
            var term = request.Customer.Trim();
            var matchingOrderIds = _context.Orders.AsNoTracking()
                .Where(o =>
                    (o.CustomerName != null && o.CustomerName.Contains(term)) ||
                    (o.GuestEmail != null && o.GuestEmail.Contains(term)) ||
                    o.UserId == term)
                .Select(o => o.Id);
            query = query.Where(r => matchingOrderIds.Contains(r.OrderId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalRefunded = await query.SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

        var rows = await query
            .Join(_context.Orders.AsNoTracking(), r => r.OrderId, o => o.Id, (r, o) => new { r, o.PublicOrderNumber })
            .OrderByDescending(x => x.r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminRefundReportRowDto(
                x.r.Id,
                x.r.ReferenceNumber,
                x.r.OrderId,
                x.PublicOrderNumber,
                x.r.Type.ToString(),
                x.r.Status.ToString(),
                x.r.Amount,
                x.r.Currency,
                x.r.InitiatedBy,
                x.r.CreatedAtUtc,
                x.r.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        var result = new AdminRefundReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalRefunded,
            RefundAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Payment report ----

    public async Task<AdminPaymentReportResult> GetPaymentReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(ReportTypes.Payments, request);
        var cached = await GetCachedAsync<AdminPaymentReportResult>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (from, to) = NormalizeRange(request);
        var (page, pageSize) = NormalizePage(request);

        var query = _context.Payments.AsNoTracking()
            .Where(p => p.CreatedAtUtc >= from && p.CreatedAtUtc < to);

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<PaymentState>(request.Status, ignoreCase: true, out var paymentState))
        {
            query = query.Where(p => p.State == paymentState);
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentMethodCode))
        {
            query = query.Where(p => p.PaymentMethodCode == request.PaymentMethodCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            query = query.Where(p => p.Currency == request.Currency);
        }

        if (!string.IsNullOrWhiteSpace(request.Customer))
        {
            var term = request.Customer.Trim();
            var matchingOrderIds = _context.Orders.AsNoTracking()
                .Where(o =>
                    (o.CustomerName != null && o.CustomerName.Contains(term)) ||
                    (o.GuestEmail != null && o.GuestEmail.Contains(term)) ||
                    o.UserId == term)
                .Select(o => o.Id);
            query = query.Where(p => matchingOrderIds.Contains(p.OrderId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalAmount = await query.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var rows = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminPaymentReportRowDto(
                p.Id,
                p.OrderId,
                p.Order != null ? p.Order.PublicOrderNumber : null,
                p.ProviderCode,
                p.PaymentMethodCode,
                p.Amount,
                p.Currency,
                p.State.ToString(),
                p.FailureCode,
                p.CreatedAtUtc,
                p.CompletedAtUtc,
                p.FailedAtUtc))
            .ToListAsync(cancellationToken);

        var result = new AdminPaymentReportResult(
            rows,
            new AdminReportPagingDto(totalCount, page, pageSize, page * pageSize < totalCount),
            totalAmount,
            PaymentAccuracyNote);

        await SetCachedAsync(key, result, 5, cancellationToken);
        return result;
    }

    // ---- Export ----

    public async Task<AdminReportExportJobDto> RequestExportAsync(
        string reportType,
        AdminReportRequest filters,
        CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var key = ExportCacheKey(jobId);

        if (!ReportTypes.All.Contains(reportType))
        {
            return new AdminReportExportJobDto(jobId, reportType, AdminReportExportStatus.Failed, null, "Unknown report type.");
        }

        var preparing = new AdminReportExportSnapshot(jobId, reportType, filters, DateTime.UtcNow, AdminReportExportStatus.Preparing);
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(preparing), GetCacheOptions(60), cancellationToken);

        try
        {
            _backgroundJobs.Enqueue<ReportExportJob>(job => job.ExecuteAsync(jobId, reportType, filters, CancellationToken.None));
            return new AdminReportExportJobDto(jobId, reportType, AdminReportExportStatus.Preparing, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not enqueue report export {JobId} ({ReportType}).", jobId, reportType);
            var failed = new AdminReportExportSnapshot(jobId, reportType, filters, DateTime.UtcNow, AdminReportExportStatus.Failed, null, ex.Message);
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(failed), GetCacheOptions(60), cancellationToken);
            return new AdminReportExportJobDto(jobId, reportType, AdminReportExportStatus.Failed, null, ex.Message);
        }
    }

    public async Task<AdminReportExportJobDto> GetExportStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(ExportCacheKey(jobId), cancellationToken);
        if (string.IsNullOrEmpty(cached))
        {
            return new AdminReportExportJobDto(jobId, string.Empty, AdminReportExportStatus.Failed, null, "Export job not found.");
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<AdminReportExportSnapshot>(cached)!;
            var fileName = snapshot.Status == AdminReportExportStatus.Ready && !string.IsNullOrEmpty(snapshot.AbsolutePath)
                ? Path.GetFileName(snapshot.AbsolutePath)
                : null;
            return new AdminReportExportJobDto(
                jobId,
                snapshot.ReportType,
                snapshot.Status,
                fileName,
                snapshot.ErrorMessage);
        }
        catch (JsonException)
        {
            return new AdminReportExportJobDto(jobId, string.Empty, AdminReportExportStatus.Failed, null, "Export job not found.");
        }
    }

    public async Task<string?> GetExportFilePathAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(ExportCacheKey(jobId), cancellationToken);
        if (string.IsNullOrEmpty(cached))
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<AdminReportExportSnapshot>(cached)!;
            return snapshot.Status == AdminReportExportStatus.Ready &&
                   !string.IsNullOrEmpty(snapshot.AbsolutePath) &&
                   System.IO.File.Exists(snapshot.AbsolutePath)
                ? snapshot.AbsolutePath
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync(CacheKeys.AdminReportFilters, cancellationToken);
    }

    // ---- Export generation ----

    public async Task BuildExportFileAsync(string jobId, string reportType, AdminReportRequest request, string? contentRootPath, CancellationToken cancellationToken = default)
    {
        var key = ExportCacheKey(jobId);
        try
        {
            var directory = ResolveExportDirectory(contentRootPath);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, jobId + ".csv");

            var (from, to) = NormalizeRange(request);
            var batchSize = Math.Max(1, _settings.Value.ExportBatchSize);

            await using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                await WriteHeaderAsync(writer, reportType, cancellationToken);

                var offset = 0;
                while (true)
                {
                    var batch = await GetExportBatchAsync(reportType, request, from, to, offset, batchSize, cancellationToken);
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    foreach (var row in batch)
                    {
                        await writer.WriteLineAsync(row);
                    }

                    if (batch.Count < batchSize)
                    {
                        break;
                    }

                    offset += batchSize;
                }
            }

            var ready = new AdminReportExportSnapshot(jobId, reportType, request, DateTime.UtcNow, AdminReportExportStatus.Ready, path);
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(ready), GetCacheOptions(60), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report export {JobId} ({ReportType}) failed.", jobId, reportType);
            var failed = new AdminReportExportSnapshot(jobId, reportType, request, DateTime.UtcNow, AdminReportExportStatus.Failed, null, ex.Message);
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(failed), GetCacheOptions(60), cancellationToken);
            throw;
        }
    }
    // ---- Query builders ----

    private IQueryable<ProductReportItem> BuildItemReportQuery(AdminReportRequest request)
    {
        var (from, to) = NormalizeRange(request);

        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus));

        var products = _context.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.BaseSku, p.CategoryId, p.BrandId, CategoryName = p.Category != null ? p.Category.Name : null, BrandName = p.Brand != null ? p.Brand.Name : null });

        return orders
            .SelectMany(o => o.Items, (o, i) => new { o.Id, i.ProductId, i.ProductName, i.Sku, i.Quantity, i.LineTotal })
            .Join(products, x => x.ProductId, p => (Guid?)p.Id, (x, p) => new ProductReportItem(
                x.ProductId ?? p.Id,
                p.Name,
                p.BaseSku,
                p.CategoryId,
                p.CategoryName,
                p.BrandId,
                p.BrandName,
                x.Quantity,
                x.LineTotal,
                1))
            .GroupBy(x => new { x.ProductId, x.ProductName, x.Sku, x.CategoryId, x.CategoryName, x.BrandId, x.BrandName })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.Sku,
                g.Key.CategoryId,
                g.Key.CategoryName,
                g.Key.BrandId,
                g.Key.BrandName,
                Units = g.Sum(x => x.Units),
                Revenue = g.Sum(x => x.Revenue),
                OrderCount = g.Sum(x => x.OrderCount)
            })
            .OrderByDescending(x => x.Revenue)
            .Select(x => new ProductReportItem(
                x.ProductId,
                x.ProductName,
                x.Sku,
                x.CategoryId,
                x.CategoryName,
                x.BrandId,
                x.BrandName,
                x.Units,
                x.Revenue,
                x.OrderCount));
    }

    private IQueryable<Order> ApplyOrderFilters(
        IQueryable<Order> query,
        AdminReportRequest request,
        DateTime fromUtc,
        DateTime toUtc)
    {
        query = query.Where(o => o.CreatedAtUtc >= fromUtc && o.CreatedAtUtc < toUtc);

        if (request.ShippingMethodId.HasValue)
        {
            query = query.Where(o => o.ShippingMethodId == request.ShippingMethodId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentMethodCode))
        {
            query = query.Where(o => o.PaymentMethodCode == request.PaymentMethodCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            query = query.Where(o => o.Currency == request.Currency);
        }

        if (!string.IsNullOrWhiteSpace(request.Customer))
        {
            var term = request.Customer.Trim();
            query = query.Where(o =>
                (o.CustomerName != null && o.CustomerName.Contains(term)) ||
                (o.GuestEmail != null && o.GuestEmail.Contains(term)) ||
                o.UserId == term);
        }

        if (request.ProductId.HasValue)
        {
            var productId = request.ProductId.Value;
            query = query.Where(o => o.Items.Any(i => i.ProductId == productId));
        }

        if (request.CategoryId.HasValue || request.BrandId.HasValue)
        {
            var productQuery = _context.Products.AsNoTracking().AsQueryable();
            if (request.CategoryId.HasValue)
            {
                var categoryId = request.CategoryId.Value;
                productQuery = productQuery.Where(p => p.CategoryId == categoryId);
            }

            if (request.BrandId.HasValue)
            {
                var brandId = request.BrandId.Value;
                productQuery = productQuery.Where(p => p.BrandId == brandId);
            }

            var matchingProductIds = productQuery.Select(p => p.Id);
            query = query.Where(o => o.Items.Any(i => i.ProductId != null && matchingProductIds.Contains(i.ProductId.Value)));
        }

        return query;
    }

    private async Task<IReadOnlyList<string>> GetExportBatchAsync(
        string reportType,
        AdminReportRequest request,
        DateTime from,
        DateTime to,
        int offset,
        int batchSize,
        CancellationToken cancellationToken)
    {
        switch (reportType)
        {
            case ReportTypes.Sales:
                return await SalesExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.Orders:
                return await OrdersExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.ProductSales:
                return await ProductExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.VariationSales:
                return await VariationExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.Category:
                return await CategoryExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.Brand:
                return await BrandExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.Customer:
                return await CustomerExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.CouponUsage:
                return await CouponExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.Inventory:
                return await InventoryExportBatchAsync(request, offset, batchSize, cancellationToken);
            case ReportTypes.Returns:
                return await ReturnsExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.Refunds:
                return await RefundsExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            case ReportTypes.Payments:
                return await PaymentsExportBatchAsync(request, from, to, offset, batchSize, cancellationToken);
            default:
                return Array.Empty<string>();
        }
    }

    private static async Task WriteHeaderAsync(TextWriter writer, string reportType, CancellationToken cancellationToken)
    {
        var header = reportType switch
        {
            ReportTypes.Sales => "DayUtc,OrderCount,GrossSales,Discounts,Shipping,Tax,Refunds,NetSales",
            ReportTypes.Orders => "OrderNumber,CustomerName,GuestEmail,Currency,GrandTotal,PaidAmount,RefundedAmount,AmountDue,OrderStatus,PaymentStatus,PaymentMethodCode,ShippingMethodName,CreatedAtUtc",
            ReportTypes.ProductSales => "ProductId,ProductName,Sku,CategoryName,BrandName,UnitsSold,OrderCount,GrossRevenue",
            ReportTypes.VariationSales => "VariantId,Sku,ProductName,VariantName,UnitsSold,OrderCount,GrossRevenue",
            ReportTypes.Category => "CategoryId,CategoryName,UnitsSold,OrderCount,GrossRevenue,RevenueShare",
            ReportTypes.Brand => "BrandId,BrandName,UnitsSold,OrderCount,GrossRevenue,RevenueShare",
            ReportTypes.Customer => "CustomerId,CustomerName,Email,OrderCount,TotalSpent,TotalRefunded,LastOrderAtUtc",
            ReportTypes.CouponUsage => "CouponId,Code,Name,UsageCount,DistinctCustomers,TotalDiscounted",
            ReportTypes.Inventory => "VariantId,Sku,ProductName,CategoryName,TotalOnHand,TotalReserved,TotalAvailable,LowStockThreshold,AllowBackorder,StockStatus",
            ReportTypes.Returns => "ReturnNumber,OrderNumber,CustomerName,GuestEmail,ReasonCode,Status,RefundableAmount,RefundedAmount,IsExchange,CreatedAtUtc",
            ReportTypes.Refunds => "ReferenceNumber,OrderNumber,OrderId,Type,Status,Amount,Currency,InitiatedBy,CreatedAtUtc,CompletedAtUtc",
            ReportTypes.Payments => "PaymentId,OrderId,OrderNumber,ProviderCode,PaymentMethodCode,Amount,Currency,State,FailureCode,CreatedAtUtc,CompletedAtUtc,FailedAtUtc",
            _ => "Report"
        };

        await writer.WriteLineAsync(header);
    }

    private async Task<IReadOnlyList<string>> SalesExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus))
            .GroupBy(o => o.CreatedAtUtc.Date)
            .Select(g => new
            {
                Day = g.Key,
                Count = g.Count(),
                Gross = g.Sum(x => x.GrandTotal),
                Discounts = g.Sum(x => x.ProductDiscount + x.CouponDiscount),
                Shipping = g.Sum(x => x.ShippingCharge),
                Tax = g.Sum(x => x.Tax),
                Refunds = g.Sum(x => x.RefundedAmount),
                Net = g.Sum(x => x.GrandTotal) - g.Sum(x => x.RefundedAmount)
            })
            .OrderByDescending(x => x.Day)
            .Skip(offset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return rows.Select(r => $"{r.Day:yyyy-MM-dd},{r.Count},{F(r.Gross)},{F(r.Discounts)},{F(r.Shipping)},{F(r.Tax)},{F(r.Refunds)},{F(r.Net)}").ToList();
    }

    private async Task<IReadOnlyList<string>> OrdersExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to);
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var st))
        {
            orders = orders.Where(o => o.OrderStatus == st);
        }

        var rows = await orders
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip(offset)
            .Take(batchSize)
            .Select(o => new AdminOrderReportRowDto(
                o.Id, o.PublicOrderNumber, o.CustomerName, o.GuestEmail, string.IsNullOrEmpty(o.UserId),
                o.Currency, o.GrandTotal, o.PaidAmount, o.RefundedAmount, o.GrandTotal - o.PaidAmount,
                o.OrderStatus.ToString(), o.PaymentStatus.ToString(), o.PaymentMethodCode, o.ShippingMethodName, o.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return rows.Select(r => $"{E(r.OrderNumber)},{E(r.CustomerName)},{E(r.GuestEmail)},{E(r.Currency)},{F(r.GrandTotal)},{F(r.PaidAmount)},{F(r.RefundedAmount)},{F(r.AmountDue)},{E(r.OrderStatus)},{E(r.PaymentStatus)},{E(r.PaymentMethodCode)},{E(r.ShippingMethodName)},{r.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}").ToList();
    }

    private async Task<IReadOnlyList<string>> ProductExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await BuildItemReportQuery(request)
            .Skip(offset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return rows.Select(r => $"{r.ProductId},{E(r.ProductName)},{E(r.Sku)},{E(r.CategoryName)},{E(r.BrandName)},{r.Units},{r.OrderCount},{F(r.Revenue)}").ToList();
    }

    private async Task<IReadOnlyList<string>> VariationExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus));

        var rows = await orders
            .SelectMany(o => o.Items.Where(i => i.ProductVariantId != null), (o, i) => new { i.ProductVariantId, i.ProductName, i.Sku, i.Quantity, i.LineTotal, i.OrderId })
            .GroupBy(x => new { x.ProductVariantId, x.ProductName, x.Sku })
            .Select(g => new
            {
                g.Key.ProductVariantId,
                g.Key.ProductName,
                g.Key.Sku,
                Units = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal),
                OrderCount = g.Select(x => x.OrderId).Distinct().Count()
            })
            .OrderByDescending(x => x.Revenue)
            .Skip(offset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var variantIds = rows.Where(x => x.ProductVariantId.HasValue).Select(x => x.ProductVariantId!.Value).ToList();
        var names = await LoadVariantNamesAsync(variantIds, cancellationToken);

        return rows.Select(r => $"{r.ProductVariantId},{E(r.Sku)},{E(r.ProductName)},{E(names.TryGetValue(r.ProductVariantId!.Value, out var n) ? n : null)},{r.Units},{r.OrderCount},{F(r.Revenue)}").ToList();
    }

    private async Task<IReadOnlyList<string>> CategoryExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await BuildItemReportQuery(request)
            .Select(x => new { x.CategoryId, x.CategoryName, x.Units, x.Revenue, x.OrderCount })
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, CategoryName = g.Max(x => x.CategoryName), Units = g.Sum(x => x.Units), Revenue = g.Sum(x => x.Revenue), OrderCount = g.Sum(x => x.OrderCount) })
            .OrderByDescending(x => x.Revenue)
            .Skip(offset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var totalRevenue = await BuildItemReportQuery(request).SumAsync(x => (decimal?)x.Revenue, cancellationToken) ?? 0m;

        return rows.Select(r => $"{r.CategoryId},{E(r.CategoryName)},{r.Units},{r.OrderCount},{F(r.Revenue)},{F(totalRevenue > 0 ? Math.Round(r.Revenue / totalRevenue * 100m, 2) : 0m)}").ToList();
    }

    private async Task<IReadOnlyList<string>> BrandExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await BuildItemReportQuery(request)
            .Select(x => new { x.BrandId, x.BrandName, x.Units, x.Revenue, x.OrderCount })
            .GroupBy(x => x.BrandId)
            .Select(g => new { BrandId = g.Key, BrandName = g.Max(x => x.BrandName), Units = g.Sum(x => x.Units), Revenue = g.Sum(x => x.Revenue), OrderCount = g.Sum(x => x.OrderCount) })
            .OrderByDescending(x => x.Revenue)
            .Skip(offset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var totalRevenue = await BuildItemReportQuery(request).SumAsync(x => (decimal?)x.Revenue, cancellationToken) ?? 0m;

        return rows.Select(r => $"{r.BrandId},{E(r.BrandName)},{r.Units},{r.OrderCount},{F(r.Revenue)},{F(totalRevenue > 0 ? Math.Round(r.Revenue / totalRevenue * 100m, 2) : 0m)}").ToList();
    }

    private async Task<IReadOnlyList<string>> CustomerExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var orders = ApplyOrderFilters(_context.Orders.AsNoTracking(), request, from, to)
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && PaidStatuses.Contains(o.PaymentStatus))
            .Where(o => o.UserId != null || o.GuestEmail != null);

        var rows = await orders
            .GroupBy(o => new { o.UserId, o.GuestEmail })
            .Select(g => new { g.Key.UserId, g.Key.GuestEmail, OrderCount = g.Count(), TotalSpent = g.Sum(x => x.GrandTotal), TotalRefunded = g.Sum(x => x.RefundedAmount), LastOrderAtUtc = g.Max(x => x.CreatedAtUtc) })
            .OrderByDescending(x => x.TotalSpent)
            .Skip(offset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var userIds = rows.Where(x => x.UserId != null).Select(x => x.UserId!).ToList();
        var emails = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Email }).ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        return rows.Select(r => $"{E(r.UserId ?? r.GuestEmail)},{E(r.GuestEmail)},{E(r.UserId != null && emails.TryGetValue(r.UserId, out var e) ? e : r.GuestEmail)},{r.OrderCount},{F(r.TotalSpent)},{F(r.TotalRefunded)},{r.LastOrderAtUtc:yyyy-MM-dd HH:mm:ss}").ToList();
    }

    private async Task<IReadOnlyList<string>> CouponExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await _context.CouponUsages.AsNoTracking()
            .Where(u => u.VoidedAtUtc == null && u.UsedAtUtc >= from && u.UsedAtUtc < to)
            .GroupBy(u => u.CouponId)
            .Select(g => new { CouponId = g.Key, UsageCount = g.Count(), Distinct = g.Select(x => x.UserId).Distinct().Count(), Total = g.Sum(x => x.AmountDiscounted) })
            .OrderByDescending(x => x.Total)
            .Skip(offset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var couponIds = rows.Select(x => x.CouponId).ToList();
        var coupons = await _context.Coupons.AsNoTracking().Where(c => couponIds.Contains(c.Id)).Select(c => new { c.Id, c.Code, c.Name }).ToDictionaryAsync(c => c.Id, c => c, cancellationToken);

        return rows.Select(r => coupons.TryGetValue(r.CouponId, out var c)
            ? $"{r.CouponId},{E(c.Code)},{E(c.Name)},{r.UsageCount},{r.Distinct},{F(r.Total)}"
            : $"{r.CouponId},Deleted,Deleted coupon,{r.UsageCount},{r.Distinct},{F(r.Total)}").ToList();
    }

    private async Task<IReadOnlyList<string>> InventoryExportBatchAsync(AdminReportRequest request, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var all = await BuildInventoryRowsAsync(cancellationToken);
        var rows = all
            .Where(x => string.IsNullOrWhiteSpace(request.Status) || string.Equals(x.StockStatus, request.Status.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Sku)
            .Skip(offset)
            .Take(batchSize)
            .ToList();

        return rows.Select(r => $"{r.VariantId},{E(r.Sku)},{E(r.ProductName)},{E(r.CategoryName)},{r.TotalOnHand},{r.TotalReserved},{r.TotalAvailable},{r.LowStockThreshold},{r.AllowBackorder},{E(r.StockStatus)}").ToList();
    }

    private async Task<IReadOnlyList<string>> ReturnsExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var query = _context.ReturnRequests.AsNoTracking().Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<ReturnStatus>(request.Status, ignoreCase: true, out var rs)) query = query.Where(r => r.Status == rs);

        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip(offset)
            .Take(batchSize)
            .Select(r => new { r.ReturnNumber, r.CustomerName, r.GuestEmail, r.ReasonCode, r.Status, r.RefundableAmount, r.RefundedAmount, r.IsExchange, r.CreatedAtUtc, OrderNumber = r.Order != null ? r.Order.PublicOrderNumber : null })
            .ToListAsync(cancellationToken);

        return rows.Select(r => $"{E(r.ReturnNumber)},{E(r.OrderNumber)},{E(r.CustomerName)},{E(r.GuestEmail)},{E(r.ReasonCode.ToString())},{E(r.Status.ToString())},{F(r.RefundableAmount)},{F(r.RefundedAmount)},{r.IsExchange},{r.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}").ToList();
    }

    private async Task<IReadOnlyList<string>> RefundsExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var query = _context.Refunds.AsNoTracking().Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<RefundStatus>(request.Status, ignoreCase: true, out var rfs)) query = query.Where(r => r.Status == rfs);

        var rows = await query
            .Join(_context.Orders.AsNoTracking(), r => r.OrderId, o => o.Id, (r, o) => new { r, o.PublicOrderNumber })
            .OrderByDescending(x => x.r.CreatedAtUtc)
            .Skip(offset)
            .Take(batchSize)
            .Select(x => new { x.r.ReferenceNumber, x.r.OrderId, OrderNumber = x.PublicOrderNumber, x.r.Type, x.r.Status, x.r.Amount, x.r.Currency, x.r.InitiatedBy, x.r.CreatedAtUtc, x.r.CompletedAtUtc })
            .ToListAsync(cancellationToken);

        return rows.Select(r => $"{E(r.ReferenceNumber)},{E(r.OrderNumber)},{r.OrderId},{E(r.Type.ToString())},{E(r.Status.ToString())},{F(r.Amount)},{E(r.Currency)},{E(r.InitiatedBy)},{r.CreatedAtUtc:yyyy-MM-dd HH:mm:ss},{r.CompletedAtUtc:yyyy-MM-dd HH:mm:ss}").ToList();
    }

    private async Task<IReadOnlyList<string>> PaymentsExportBatchAsync(AdminReportRequest request, DateTime from, DateTime to, int offset, int batchSize, CancellationToken cancellationToken)
    {
        var query = _context.Payments.AsNoTracking().Where(p => p.CreatedAtUtc >= from && p.CreatedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<PaymentState>(request.Status, ignoreCase: true, out var ps)) query = query.Where(p => p.State == ps);
        if (!string.IsNullOrWhiteSpace(request.PaymentMethodCode)) query = query.Where(p => p.PaymentMethodCode == request.PaymentMethodCode);

        var rows = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip(offset)
            .Take(batchSize)
            .Select(p => new { p.Id, p.OrderId, p.ProviderCode, p.PaymentMethodCode, p.Amount, p.Currency, p.State, p.FailureCode, p.CreatedAtUtc, p.CompletedAtUtc, p.FailedAtUtc, OrderNumber = p.Order != null ? p.Order.PublicOrderNumber : null })
            .ToListAsync(cancellationToken);

        return rows.Select(r => $"{r.Id},{r.OrderId},{E(r.OrderNumber)},{E(r.ProviderCode)},{E(r.PaymentMethodCode)},{F(r.Amount)},{E(r.Currency)},{E(r.State.ToString())},{E(r.FailureCode)},{r.CreatedAtUtc:yyyy-MM-dd HH:mm:ss},{r.CompletedAtUtc:yyyy-MM-dd HH:mm:ss},{r.FailedAtUtc:yyyy-MM-dd HH:mm:ss}").ToList();
    }

    private async Task<IReadOnlyList<InventoryReportRow>> BuildInventoryRowsAsync(CancellationToken cancellationToken)
    {
        var groups = await _context.WarehouseStocks.AsNoTracking()
            .GroupBy(s => s.ProductVariantId)
            .Select(g => new { VariantId = g.Key, OnHand = g.Sum(x => x.OnHandQuantity), Reserved = g.Sum(x => x.ReservedQuantity), MinThreshold = g.Min(x => x.LowStockThreshold), AllowBackorder = g.Max(x => x.AllowBackorder ? 1 : 0) })
            .ToListAsync(cancellationToken);

        var variantIds = groups.Select(x => x.VariantId).ToList();
        var variants = await _context.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, v.Sku, v.ProductId }).ToListAsync(cancellationToken);
        var productIds = variants.Select(v => v.ProductId).ToList();
        var products = await _context.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Name, p.CategoryId, p.BrandId }).ToListAsync(cancellationToken);
        var categoryIds = products.Select(p => p.CategoryId).ToList();
        var categories = await _context.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var variantById = variants.ToDictionary(v => v.Id, v => v);
        var productById = products.ToDictionary(p => p.Id, p => p);

        return groups.Select(g =>
        {
            var available = g.OnHand - g.Reserved;
            var variant = variantById.GetValueOrDefault(g.VariantId);
            var product = variant != null ? productById.GetValueOrDefault(variant.ProductId) : null;
            var status = available <= 0 ? "OutOfStock" : (g.MinThreshold.HasValue && available <= g.MinThreshold.Value ? "LowStock" : "InStock");
            return new InventoryReportRow(
                g.VariantId,
                variant?.ProductId ?? Guid.Empty,
                variant?.Sku ?? "Unknown",
                product?.Name ?? "Unknown",
                product?.CategoryId,
                product != null && categories.TryGetValue(product.CategoryId, out var cn) ? cn : null,
                product?.BrandId,
                g.OnHand,
                g.Reserved,
                available,
                g.MinThreshold,
                g.AllowBackorder == 1,
                status);
        }).ToList();
    }

    // ---- Shared helpers ----

    private sealed record ProductReportItem(
        Guid ProductId,
        string ProductName,
        string? Sku,
        Guid CategoryId,
        string? CategoryName,
        Guid? BrandId,
        string? BrandName,
        int Units,
        decimal Revenue,
        int OrderCount);

    private sealed record InventoryReportRow(
        Guid VariantId,
        Guid ProductId,
        string Sku,
        string ProductName,
        Guid? CategoryId,
        string? CategoryName,
        Guid? BrandId,
        int TotalOnHand,
        int TotalReserved,
        int TotalAvailable,
        int? LowStockThreshold,
        bool AllowBackorder,
        string StockStatus);

    private (DateTime FromUtc, DateTime ToUtc) NormalizeRange(AdminReportRequest request)
    {
        var maxDays = Math.Max(1, _settings.Value.MaxDateRangeDays);
        var to = request.DateToUtc ?? DateTime.UtcNow.Date.AddDays(1);
        var from = request.DateFromUtc ?? to.AddDays(-Math.Min(30, maxDays));
        if (from > to)
        {
            (from, to) = (to, from);
        }

        if ((to - from).TotalDays > maxDays)
        {
            from = to.AddDays(-maxDays);
        }

        return (from, to);
    }

    private static (int Page, int PageSize) NormalizePage(AdminReportRequest request)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);
        return (page, pageSize);
    }

    private async Task<Dictionary<Guid, string>> LoadVariantNamesAsync(IReadOnlyList<Guid> variantIds, CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var values = await _context.ProductVariantAttributeValues.AsNoTracking()
            .Where(vav => variantIds.Contains(vav.ProductVariantId) && vav.AttributeValue != null)
            .GroupBy(vav => vav.ProductVariantId)
            .Select(g => new { VariantId = g.Key, Name = string.Join(", ", g.Select(x => x.AttributeValue!.Name)) })
            .ToListAsync(cancellationToken);

        return values.ToDictionary(v => v.VariantId, v => v.Name);
    }

    private async Task<T?> GetCachedAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(cached))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stale report cache could not be deserialized for key {Key}.", key);
            return null;
        }
    }

    private Task SetCachedAsync<T>(string key, T value, int minutes, CancellationToken cancellationToken)
    {
        return _cache.SetStringAsync(key, JsonSerializer.Serialize(value), GetCacheOptions(minutes), cancellationToken);
    }

    private static string BuildCacheKey(string reportType, AdminReportRequest request)
    {
        var key = string.Join("|",
            request.DateFromUtc?.ToString("O") ?? string.Empty,
            request.DateToUtc?.ToString("O") ?? string.Empty,
            request.Status ?? string.Empty,
            request.CategoryId?.ToString() ?? string.Empty,
            request.BrandId?.ToString() ?? string.Empty,
            request.ProductId?.ToString() ?? string.Empty,
            request.PaymentMethodCode ?? string.Empty,
            request.ShippingMethodId?.ToString() ?? string.Empty,
            request.Customer ?? string.Empty,
            request.Currency ?? string.Empty,
            request.Page,
            request.PageSize);

        return CacheKeys.AdminReport
            .Replace("{type}", reportType)
            .Replace("{key}", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..16]);
    }

    private string ResolveExportDirectory(string? contentRootPath)
    {
        var directory = _settings.Value.ExportDirectory;
        return Path.IsPathRooted(directory) ? directory : Path.Combine(contentRootPath ?? AppContext.BaseDirectory, directory);
    }

    private static string ExportCacheKey(string jobId) => CacheKeys.AdminReportExport.Replace("{jobId}", jobId);

    private static DistributedCacheEntryOptions GetCacheOptions(int minutes) => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(minutes)
    };

    private static string F(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string E(string? value) => value is null ? string.Empty : value.Replace("\"", "\"\"");
}
