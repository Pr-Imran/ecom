using FashionStore.Application.Common.Models;
using FashionStore.Application.DTOs.Seo;
using FashionStore.Domain.Enums;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Resolves and manages permanent slug redirects. Public catalogue/content
/// controllers consult <see cref="ResolveAsync"/> when a slug no longer matches
/// so renamed slugs issue a 301 (or 410 when the entity is gone) instead of a
/// 404, preserving search equity and deep links.
/// </summary>
public interface ISlugRedirectService
{
    /// <summary>Returns the redirect target slug for <paramref name="slug"/> or null when none exists.</summary>
    Task<string?> ResolveAsync(SlugEntityType entityType, string slug, CancellationToken cancellationToken = default);

    Task<SlugRedirectDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SlugRedirectDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new redirect or updates the existing one for the entity/slug pair.</summary>
    Task<Result<Guid>> AddOrUpdateAsync(SlugRedirectRequest request, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
