using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Promotions;

/// <summary>
/// Promotion as exposed to administrators. Promotions are auto-applied catalog
/// discounts with a scope (product / category / brand / collection), an optional
/// minimum quantity trigger, a priority ordering and a stackable flag.
/// </summary>
public sealed record PromotionDto(
    Guid Id,
    string Name,
    string? Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    int MinQuantity,
    int Priority,
    bool IsStackable,
    bool IsActive,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    Guid? ProductId,
    Guid? CategoryId,
    Guid? BrandId,
    Guid? CollectionId,
    DateTime CreatedAtUtc
);

/// <summary>
/// Request used to create a promotion. Activation state is managed separately
/// and defaults to active on creation.
/// </summary>
public sealed record CreatePromotionRequest(
    string Name,
    string? Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    int MinQuantity,
    int Priority,
    bool IsStackable,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    Guid? ProductId,
    Guid? CategoryId,
    Guid? BrandId,
    Guid? CollectionId
);

/// <summary>
/// Request used to update an existing promotion.
/// </summary>
public sealed record UpdatePromotionRequest(
    Guid Id,
    string Name,
    string? Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    int MinQuantity,
    int Priority,
    bool IsStackable,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    Guid? ProductId,
    Guid? CategoryId,
    Guid? BrandId,
    Guid? CollectionId
);
