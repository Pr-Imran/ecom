using FashionStore.Application.DTOs.Settings;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Reads and writes the strongly typed store-wide website settings. Reads are
/// served from a distributed cache (invalidated on every write); writes are
/// audited and protected settings are rejected for non-SuperAdmin callers.
/// </summary>
public interface IWebsiteSettingsService
{
    Task<WebsiteSettingsSnapshot> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<SettingsMutationResult> UpdateSettingsAsync(UpdateWebsiteSettingsRequest request, string actorId, bool isSuperAdmin, CancellationToken cancellationToken = default);
    Task InvalidateSettingsCacheAsync(CancellationToken cancellationToken = default);
}
