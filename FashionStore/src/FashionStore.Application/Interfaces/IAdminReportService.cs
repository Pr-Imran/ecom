using FashionStore.Application.DTOs.Reports;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Aggregated administrative reports (sales, orders, products, variations,
/// categories, brands, customers, coupons, inventory, returns, refunds and
/// payments). Every report is produced with database-side aggregation and
/// projection, is always date-limited, paginated and cached, and never loads the
/// full order set into memory. Exports are prepared in the background and the
/// resulting file is streamed to the caller on download.
/// </summary>
public interface IAdminReportService
{
    Task<AdminReportFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    Task<AdminSalesReportResult> GetSalesReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminOrderReportResult> GetOrderReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminProductSalesReportResult> GetProductSalesReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminVariationSalesReportResult> GetVariationSalesReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminCategoryReportResult> GetCategoryReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminBrandReportResult> GetBrandReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminCustomerReportResult> GetCustomerReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminCouponUsageReportResult> GetCouponUsageReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminInventoryReportResult> GetInventoryReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminReturnReportResult> GetReturnReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminRefundReportResult> GetRefundReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);
    Task<AdminPaymentReportResult> GetPaymentReportAsync(AdminReportRequest request, CancellationToken cancellationToken = default);

    Task<AdminReportExportJobDto> RequestExportAsync(
        string reportType,
        AdminReportRequest filters,
        CancellationToken cancellationToken = default);

    Task<AdminReportExportJobDto> GetExportStatusAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Absolute path of the prepared export file when the job is Ready, otherwise null.</summary>
    Task<string?> GetExportFilePathAsync(string jobId, CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
}
