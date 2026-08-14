using FashionStore.Application.DTOs.Reports;
using FashionStore.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;

namespace FashionStore.Infrastructure.BackgroundJobs;

/// <summary>
/// Prepares a report export in the background by streaming the matching rows to a
/// CSV file. The job runs on the Hangfire worker so large exports never block a
/// request; progress is reported through the report service's export status
/// endpoint, which hands the finished file to the browser for download.
/// </summary>
public sealed class ReportExportJob
{
    private readonly AdminReportService _reportService;
    private readonly IWebHostEnvironment _environment;

    public ReportExportJob(AdminReportService reportService, IWebHostEnvironment environment)
    {
        _reportService = reportService;
        _environment = environment;
    }

    public Task ExecuteAsync(
        string jobId,
        string reportType,
        AdminReportRequest filters,
        CancellationToken cancellationToken = default)
    {
        return _reportService.BuildExportFileAsync(jobId, reportType, filters, _environment.ContentRootPath, cancellationToken);
    }
}
