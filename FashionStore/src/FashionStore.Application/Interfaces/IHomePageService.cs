using FashionStore.Application.DTOs.Home;

namespace FashionStore.Application.Interfaces;

public interface IHomePageService
{
    Task<HomePageData> GetHomePageAsync(CancellationToken cancellationToken = default);
    Task InvalidateHomePageCacheAsync(CancellationToken cancellationToken = default);
}
