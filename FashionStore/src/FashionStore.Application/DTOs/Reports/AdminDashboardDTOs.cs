using FashionStore.Application.DTOs.Orders;

namespace FashionStore.Application.DTOs.Reports;

/// <summary>
/// One headline metric on the administration dashboard.
/// </summary>
public sealed record AdminDashboardMetricsDto(
    decimal SalesToday,
    decimal SalesThisMonth,
    int OrdersToday,
    int PendingOrders,
    int PaidOrders,
    int FailedPayments,
    int LowStockVariants,
    int OutOfStockVariants,
    int PendingReturns,
    int PendingRefunds,
    int NewCustomers,
    decimal AverageOrderValue,
    string Currency);

/// <summary>One point on the sales trend chart (daily buckets, UTC).</summary>
public sealed record AdminSalesTrendPointDto(
    DateTime DayUtc,
    int OrderCount,
    decimal GrossSales);

/// <summary>Top selling product by units sold in the period.</summary>
public sealed record AdminTopProductDto(
    Guid? ProductId,
    string ProductName,
    string? Sku,
    int UnitsSold,
    decimal Revenue);

/// <summary>Top category by units sold in the period.</summary>
public sealed record AdminTopCategoryDto(
    Guid CategoryId,
    string CategoryName,
    int UnitsSold,
    decimal Revenue);

/// <summary>Top brand by units sold in the period.</summary>
public sealed record AdminTopBrandDto(
    Guid? BrandId,
    string BrandName,
    int UnitsSold,
    decimal Revenue);

/// <summary>
/// The full dashboard payload. Financial figures are gross sales captured from
/// non-cancelled, paid (or partially paid) orders. Refunded and partially
/// refunded orders remain included in gross sales so refund activity is tracked
/// separately; the <see cref="AccuracyNotes"/> list documents exactly what every
/// metric includes and excludes.
/// </summary>
public sealed record AdminDashboardDataDto(
    AdminDashboardMetricsDto Metrics,
    IReadOnlyList<AdminTopProductDto> TopProducts,
    IReadOnlyList<AdminTopCategoryDto> TopCategories,
    IReadOnlyList<AdminTopBrandDto> TopBrands,
    IReadOnlyList<AdminOrderListItemDto> RecentOrders,
    IReadOnlyList<AdminSalesTrendPointDto> SalesTrend,
    IReadOnlyList<string> AccuracyNotes);
