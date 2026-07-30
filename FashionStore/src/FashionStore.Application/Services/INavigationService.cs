using FashionStore.Application.DTOs.Navigation;

namespace FashionStore.Application.Services;

public interface INavigationService
{
    Task<IEnumerable<NavigationItem>> GetPublicNavigationAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<NavigationItem>> GetMobileNavigationAsync(string? userId = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<NavigationItem>> GetAdminNavigationAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<NavigationItem>> GetAccountNavigationAsync(string userId, CancellationToken cancellationToken = default);
    IEnumerable<BreadcrumbItem> GenerateBreadcrumbs(IEnumerable<(string Label, string? Url)> segments, string? currentPage = null);
    Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default);
    CartSummary GetCartSummary(string? userId = null);
    IEnumerable<Announcement> GetActiveAnnouncements();
}
