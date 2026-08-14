using FashionStore.Application.DTOs.Reports;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Produces the administration dashboard payload. All queries are aggregated in
/// the database (no order rows are loaded into memory) and the payload is cached
/// with a short lifetime; callers that mutate order, payment, return, refund or
/// customer data can invalidate the cache through <see cref="InvalidateCacheAsync"/>.
/// </summary>
public interface IAdminDashboardService
{
    Task<AdminDashboardDataDto> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
}
