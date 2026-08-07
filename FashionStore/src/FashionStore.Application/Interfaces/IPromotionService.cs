using FashionStore.Application.DTOs.Promotions;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Administrative promotion management. Promotions are auto-applied catalog
/// discounts scoped to a product, category, brand or collection with an optional
/// minimum quantity trigger, a priority ordering and a stackable flag.
/// </summary>
public interface IPromotionService
{
    /// <summary>
    /// Lists all promotions. When <paramref name="includeInactive"/> is false only
    /// currently active promotions (active flag and valid date window) are returned.
    /// </summary>
    Task<IReadOnlyList<PromotionDto>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single promotion or null when it does not exist.
    /// </summary>
    Task<PromotionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a promotion, defaulting to the active state.
    /// </summary>
    Task<PromotionDto> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing promotion. Returns null when it does not exist.
    /// </summary>
    Task<PromotionDto?> UpdateAsync(Guid id, UpdatePromotionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates or deactivates a promotion. Returns false when it does not exist.
    /// </summary>
    Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a copy of an existing promotion. Returns null when the original does
    /// not exist.
    /// </summary>
    Task<PromotionDto?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default);
}
