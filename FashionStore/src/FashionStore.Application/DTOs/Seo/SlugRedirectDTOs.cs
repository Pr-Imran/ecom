using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Seo;

/// <summary>
/// A permanent slug redirect used to keep deep links and search indexes working
/// after a product, category, brand, collection or page slug is renamed.
/// </summary>
public sealed record SlugRedirectDto(
    Guid Id,
    SlugEntityType EntityType,
    string OldSlug,
    string NewSlug,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>
/// Payload for creating or updating a slug redirect. An empty <c>NewSlug</c>
/// marks the entity as permanently removed (resolves to a 410 response).
/// </summary>
public sealed record SlugRedirectRequest(
    SlugEntityType EntityType,
    string OldSlug,
    string NewSlug);
