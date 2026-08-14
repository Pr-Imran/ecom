namespace FashionStore.Application.DTOs.Reports;

/// <summary>Well-known report type codes used across the reports page.</summary>
public static class ReportTypes
{
    public const string Sales = "sales";
    public const string Orders = "orders";
    public const string ProductSales = "product-sales";
    public const string VariationSales = "variation-sales";
    public const string Category = "category";
    public const string Brand = "brand";
    public const string Customer = "customer";
    public const string CouponUsage = "coupon-usage";
    public const string Inventory = "inventory";
    public const string Returns = "returns";
    public const string Refunds = "refunds";
    public const string Payments = "payments";

    public static readonly string[] All =
    {
        Sales, Orders, ProductSales, VariationSales, Category, Brand,
        Customer, CouponUsage, Inventory, Returns, Refunds, Payments
    };
}

/// <summary>
/// Common report query. Every filter is optional. Dates are inclusive UTC
/// boundaries resolved from the admin's local date pickers by the reporting
/// controller using the store timezone. A date range is always enforced so a
/// careless query cannot scan the entire order table.
/// </summary>
public sealed record AdminReportRequest(
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    string? Status,
    Guid? CategoryId,
    Guid? BrandId,
    Guid? ProductId,
    string? PaymentMethodCode,
    Guid? ShippingMethodId,
    string? Customer,
    string? Currency,
    int Page = 1,
    int PageSize = 20);

/// <summary>Shared paging envelope for every report result.</summary>
public sealed record AdminReportPagingDto(
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore);

/// <summary>Totals header for the sales report.</summary>
public sealed record AdminSalesReportTotalsDto(
    int OrderCount,
    decimal GrossSales,
    decimal Discounts,
    decimal Shipping,
    decimal Tax,
    decimal Refunds,
    decimal NetSales);

/// <summary>One day bucket of the sales report.</summary>
public sealed record AdminSalesReportRowDto(
    DateTime DayUtc,
    int OrderCount,
    decimal GrossSales,
    decimal Discounts,
    decimal Shipping,
    decimal Tax,
    decimal Refunds,
    decimal NetSales);

public sealed record AdminSalesReportResult(
    IReadOnlyList<AdminSalesReportRowDto> Items,
    AdminReportPagingDto Paging,
    AdminSalesReportTotalsDto Totals,
    string AccuracyNote);

/// <summary>One order on the order report.</summary>
public sealed record AdminOrderReportRowDto(
    Guid OrderId,
    string OrderNumber,
    string? CustomerName,
    string? GuestEmail,
    bool IsGuest,
    string Currency,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RefundedAmount,
    decimal AmountDue,
    string OrderStatus,
    string PaymentStatus,
    string? PaymentMethodCode,
    string? ShippingMethodName,
    DateTime CreatedAtUtc);

public sealed record AdminOrderReportResult(
    IReadOnlyList<AdminOrderReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalGrandTotal,
    string AccuracyNote);

/// <summary>One product on the product sales report.</summary>
public sealed record AdminProductSalesReportRowDto(
    Guid? ProductId,
    string ProductName,
    string? Sku,
    string? CategoryName,
    string? BrandName,
    int UnitsSold,
    int OrderCount,
    decimal GrossRevenue);

public sealed record AdminProductSalesReportResult(
    IReadOnlyList<AdminProductSalesReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalGrossRevenue,
    int TotalUnits,
    string AccuracyNote);

/// <summary>One variation on the variation sales report.</summary>
public sealed record AdminVariationSalesReportRowDto(
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    string? VariantName,
    int UnitsSold,
    int OrderCount,
    decimal GrossRevenue);

public sealed record AdminVariationSalesReportResult(
    IReadOnlyList<AdminVariationSalesReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalGrossRevenue,
    int TotalUnits,
    string AccuracyNote);

/// <summary>One category on the category report.</summary>
public sealed record AdminCategoryReportRowDto(
    Guid CategoryId,
    string CategoryName,
    int UnitsSold,
    int OrderCount,
    decimal GrossRevenue,
    decimal RevenueShare);

public sealed record AdminCategoryReportResult(
    IReadOnlyList<AdminCategoryReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalGrossRevenue,
    int TotalUnits,
    string AccuracyNote);

/// <summary>One brand on the brand report.</summary>
public sealed record AdminBrandReportRowDto(
    Guid? BrandId,
    string BrandName,
    int UnitsSold,
    int OrderCount,
    decimal GrossRevenue,
    decimal RevenueShare);

public sealed record AdminBrandReportResult(
    IReadOnlyList<AdminBrandReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalGrossRevenue,
    int TotalUnits,
    string AccuracyNote);

/// <summary>One customer on the customer report.</summary>
public sealed record AdminCustomerReportRowDto(
    string? CustomerId,
    string? CustomerName,
    string? Email,
    int OrderCount,
    decimal TotalSpent,
    decimal TotalRefunded,
    DateTime? LastOrderAtUtc);

public sealed record AdminCustomerReportResult(
    IReadOnlyList<AdminCustomerReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalSpent,
    string AccuracyNote);

/// <summary>One coupon on the coupon usage report.</summary>
public sealed record AdminCouponUsageReportRowDto(
    Guid CouponId,
    string Code,
    string Name,
    int UsageCount,
    int DistinctCustomers,
    decimal TotalDiscounted);

public sealed record AdminCouponUsageReportResult(
    IReadOnlyList<AdminCouponUsageReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalDiscounted,
    string AccuracyNote);

/// <summary>One variant row on the inventory report.</summary>
public sealed record AdminInventoryReportRowDto(
    Guid VariantId,
    string Sku,
    string ProductName,
    string? CategoryName,
    int TotalOnHand,
    int TotalReserved,
    int TotalAvailable,
    int? LowStockThreshold,
    bool AllowBackorder,
    string StockStatus);

public sealed record AdminInventoryReportResult(
    IReadOnlyList<AdminInventoryReportRowDto> Items,
    AdminReportPagingDto Paging,
    int TotalVariants,
    int TotalUnits,
    string AccuracyNote);

/// <summary>One return request on the return report.</summary>
public sealed record AdminReturnReportRowDto(
    Guid ReturnId,
    string ReturnNumber,
    string? OrderNumber,
    string? CustomerName,
    string? GuestEmail,
    string ReasonCode,
    string Status,
    decimal RefundableAmount,
    decimal RefundedAmount,
    bool IsExchange,
    DateTime CreatedAtUtc);

public sealed record AdminReturnReportResult(
    IReadOnlyList<AdminReturnReportRowDto> Items,
    AdminReportPagingDto Paging,
    int TotalReturned,
    decimal TotalRefundable,
    string AccuracyNote);

/// <summary>One refund on the refund report.</summary>
public sealed record AdminRefundReportRowDto(
    Guid RefundId,
    string ReferenceNumber,
    Guid OrderId,
    string? OrderNumber,
    string Type,
    string Status,
    decimal Amount,
    string Currency,
    string? InitiatedBy,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record AdminRefundReportResult(
    IReadOnlyList<AdminRefundReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalRefunded,
    string AccuracyNote);

/// <summary>One payment record on the payment report.</summary>
public sealed record AdminPaymentReportRowDto(
    Guid PaymentId,
    Guid OrderId,
    string? OrderNumber,
    string ProviderCode,
    string PaymentMethodCode,
    decimal Amount,
    string Currency,
    string State,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? FailedAtUtc);

public sealed record AdminPaymentReportResult(
    IReadOnlyList<AdminPaymentReportRowDto> Items,
    AdminReportPagingDto Paging,
    decimal TotalAmount,
    string AccuracyNote);

/// <summary>One option (id + display name) for a report filter dropdown.</summary>
public sealed record AdminReportOptionDto(Guid Id, string Name);

/// <summary>Customer options keyed by user id (or guest email) for the filter.</summary>
public sealed record AdminReportCustomerOptionDto(string Id, string Name);

/// <summary>Filter options loaded once for the reports filter sheet.</summary>
public sealed record AdminReportFilterOptions(
    IReadOnlyList<AdminReportOptionDto> Categories,
    IReadOnlyList<AdminReportOptionDto> Brands,
    IReadOnlyList<AdminReportOptionDto> Products,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<AdminReportOptionDto> ShippingMethods,
    IReadOnlyList<AdminReportCustomerOptionDto> Customers,
    IReadOnlyList<string> Currencies);

/// <summary>Lifecycle of a background report export job.</summary>
public enum AdminReportExportStatus
{
    Preparing = 0,
    Ready = 1,
    Failed = 2
}

/// <summary>Status of a background report export job.</summary>
public sealed record AdminReportExportJobDto(
    string JobId,
    string ReportType,
    AdminReportExportStatus Status,
    string? FileName,
    string? ErrorMessage);

/// <summary>Immutable snapshot of a background export job persisted in the cache.</summary>
public sealed record AdminReportExportSnapshot(
    string JobId,
    string ReportType,
    AdminReportRequest Filters,
    DateTime RequestedAtUtc,
    AdminReportExportStatus Status = AdminReportExportStatus.Preparing,
    string? AbsolutePath = null,
    string? ErrorMessage = null);
