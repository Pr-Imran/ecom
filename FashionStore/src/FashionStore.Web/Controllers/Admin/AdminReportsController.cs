using FashionStore.Application.Authorization;
using FashionStore.Application.Common;
using FashionStore.Application.DTOs.Reports;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative reporting API. Every endpoint is guarded by the
/// <c>Reports.View</c> permission. Report queries accept an optional local date
/// range (resolved to inclusive UTC boundaries using the store timezone so
/// "today" and "this month" follow the store's local day) plus status, category,
/// brand, product, payment method, shipping method, customer and currency
/// filters. Exports are prepared in the background and streamed back on download.
/// </summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize(Policy = ReportsPolicies.ReportsView)]
public class AdminReportsController : ControllerBase
{
    private readonly IAdminReportService _reportService;
    private readonly IWebsiteSettingsService _settingsService;

    public AdminReportsController(
        IAdminReportService reportService,
        IWebsiteSettingsService settingsService)
    {
        _reportService = reportService;
        _settingsService = settingsService;
    }

    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters(CancellationToken cancellationToken = default)
    {
        return Ok(await _reportService.GetFilterOptionsAsync(cancellationToken));
    }

    [HttpGet("{type}")]
    public async Task<IActionResult> GetReport(
        [FromRoute] string type,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? brandId,
        [FromQuery] Guid? productId,
        [FromQuery] string? paymentMethod,
        [FromQuery] Guid? shippingMethodId,
        [FromQuery] string? customer,
        [FromQuery] string? currency,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = await BuildRequestAsync(
            from, to, status, categoryId, brandId, productId, paymentMethod,
            shippingMethodId, customer, currency, page, pageSize, cancellationToken);

        return type switch
        {
            ReportTypes.Sales => Ok(await _reportService.GetSalesReportAsync(request, cancellationToken)),
            ReportTypes.Orders => Ok(await _reportService.GetOrderReportAsync(request, cancellationToken)),
            ReportTypes.ProductSales => Ok(await _reportService.GetProductSalesReportAsync(request, cancellationToken)),
            ReportTypes.VariationSales => Ok(await _reportService.GetVariationSalesReportAsync(request, cancellationToken)),
            ReportTypes.Category => Ok(await _reportService.GetCategoryReportAsync(request, cancellationToken)),
            ReportTypes.Brand => Ok(await _reportService.GetBrandReportAsync(request, cancellationToken)),
            ReportTypes.Customer => Ok(await _reportService.GetCustomerReportAsync(request, cancellationToken)),
            ReportTypes.CouponUsage => Ok(await _reportService.GetCouponUsageReportAsync(request, cancellationToken)),
            ReportTypes.Inventory => Ok(await _reportService.GetInventoryReportAsync(request, cancellationToken)),
            ReportTypes.Returns => Ok(await _reportService.GetReturnReportAsync(request, cancellationToken)),
            ReportTypes.Refunds => Ok(await _reportService.GetRefundReportAsync(request, cancellationToken)),
            ReportTypes.Payments => Ok(await _reportService.GetPaymentReportAsync(request, cancellationToken)),
            _ => NotFound(new { error = "Unknown report type." })
        };
    }

    [HttpPost("{type}/export")]
    public async Task<IActionResult> RequestExport(
        [FromRoute] string type,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? brandId,
        [FromQuery] Guid? productId,
        [FromQuery] string? paymentMethod,
        [FromQuery] Guid? shippingMethodId,
        [FromQuery] string? customer,
        [FromQuery] string? currency,
        CancellationToken cancellationToken = default)
    {
        var request = await BuildRequestAsync(
            from, to, status, categoryId, brandId, productId, paymentMethod,
            shippingMethodId, customer, currency, 1, 100, cancellationToken);

        var result = await _reportService.RequestExportAsync(type, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("export/{jobId}")]
    public async Task<IActionResult> GetExportStatus([FromRoute] string jobId, CancellationToken cancellationToken = default)
    {
        var status = await _reportService.GetExportStatusAsync(jobId, cancellationToken);
        return status.Status == AdminReportExportStatus.Failed && status.ErrorMessage == "Export job not found."
            ? NotFound(status)
            : Ok(status);
    }

    [HttpGet("export/{jobId}/download")]
    public async Task<IActionResult> DownloadExport([FromRoute] string jobId, CancellationToken cancellationToken = default)
    {
        var path = await _reportService.GetExportFilePathAsync(jobId, cancellationToken);
        if (path is null)
        {
            return NotFound(new { error = "Export is not ready or has expired." });
        }

        return PhysicalFile(path, "text/csv", Path.GetFileName(path));
    }

    private async Task<AdminReportRequest> BuildRequestAsync(
        DateOnly? from,
        DateOnly? to,
        string? status,
        Guid? categoryId,
        Guid? brandId,
        Guid? productId,
        string? paymentMethod,
        Guid? shippingMethodId,
        string? customer,
        string? currency,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        var (fromUtc, toUtc) = ReportDateRangeHelper.ResolveUtcRange(from, to, settings.Commerce.Timezone);

        return new AdminReportRequest(
            fromUtc,
            toUtc,
            string.IsNullOrWhiteSpace(status) ? null : status,
            categoryId,
            brandId,
            productId,
            string.IsNullOrWhiteSpace(paymentMethod) ? null : paymentMethod,
            shippingMethodId,
            string.IsNullOrWhiteSpace(customer) ? null : customer,
            string.IsNullOrWhiteSpace(currency) ? null : currency,
            Math.Max(1, page),
            Math.Max(1, Math.Min(100, pageSize)));
    }
}
