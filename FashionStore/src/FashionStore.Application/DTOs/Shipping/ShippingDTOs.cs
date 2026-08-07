using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Shipping;

/// <summary>
/// A shipping method as exposed to administrators. Restriction rows are flattened
/// into product / category lists with an exclusion flag so the client can render a
/// single scoping editor. All prices and thresholds are server-managed.
/// </summary>
public sealed record ShippingMethodDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    ShippingMethodType Type,
    bool IsActive,
    int DisplayOrder,
    int EstimatedMinDays,
    int EstimatedMaxDays,
    bool SupportsCashOnDelivery,
    bool RequiresShippingAddress,
    decimal? FreeShippingThreshold,
    decimal? MaxPackageWeight,
    string? PickupInstructions,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ShippingMethodProductRestrictionDto> ProductRestrictions,
    IReadOnlyList<ShippingMethodCategoryRestrictionDto> CategoryRestrictions);

/// <summary>
/// A product scoping entry for a shipping method. <see cref="IsExclusion"/> selects
/// between "only applies to carts containing this product" and "never applies to
/// carts containing this product".
/// </summary>
public sealed record ShippingMethodProductRestrictionDto(Guid ProductId, bool IsExclusion);

/// <summary>
/// A category scoping entry for a shipping method, mirroring the product rule.
/// </summary>
public sealed record ShippingMethodCategoryRestrictionDto(Guid CategoryId, bool IsExclusion);

/// <summary>
/// Request used to create a shipping method. The code is normalized to upper case
/// before storage so it stays unique and case-insensitive.
/// </summary>
public sealed record CreateShippingMethodRequest(
    string Code,
    string Name,
    string? Description,
    ShippingMethodType Type,
    int EstimatedMinDays,
    int EstimatedMaxDays,
    bool SupportsCashOnDelivery,
    bool RequiresShippingAddress,
    decimal? FreeShippingThreshold,
    decimal? MaxPackageWeight,
    string? PickupInstructions,
    IReadOnlyList<ShippingMethodProductRestrictionDto> ProductRestrictions,
    IReadOnlyList<ShippingMethodCategoryRestrictionDto> CategoryRestrictions);

/// <summary>
/// Request used to update an existing shipping method. Activation is toggled
/// separately so edits and activation remain independent operations.
/// </summary>
public sealed record UpdateShippingMethodRequest(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    ShippingMethodType Type,
    bool IsActive,
    int DisplayOrder,
    int EstimatedMinDays,
    int EstimatedMaxDays,
    bool SupportsCashOnDelivery,
    bool RequiresShippingAddress,
    decimal? FreeShippingThreshold,
    decimal? MaxPackageWeight,
    string? PickupInstructions,
    IReadOnlyList<ShippingMethodProductRestrictionDto> ProductRestrictions,
    IReadOnlyList<ShippingMethodCategoryRestrictionDto> CategoryRestrictions);

/// <summary>
/// A shipping zone as exposed to administrators: name, active state, ordering and
/// the flattened country / city membership lists.
/// </summary>
public sealed record ShippingZoneDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int DisplayOrder,
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> Cities,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateShippingZoneRequest(
    string Name,
    string? Description,
    int DisplayOrder,
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> Cities);

public sealed record UpdateShippingZoneRequest(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int DisplayOrder,
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> Cities);

/// <summary>
/// A shipping rate as exposed to administrators. The zone, city and weight-band
/// scoping are flattened onto a single record.
/// </summary>
public sealed record ShippingRateDto(
    Guid Id,
    Guid ShippingMethodId,
    Guid? ShippingZoneId,
    string? CityName,
    string Name,
    ShippingRateType RateType,
    decimal Amount,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    decimal? MinOrderAmount,
    int Priority);

public sealed record CreateShippingRateRequest(
    Guid ShippingMethodId,
    Guid? ShippingZoneId,
    string? CityName,
    string Name,
    ShippingRateType RateType,
    decimal Amount,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    decimal? MinOrderAmount,
    int Priority);

public sealed record UpdateShippingRateRequest(
    Guid Id,
    Guid ShippingMethodId,
    Guid? ShippingZoneId,
    string? CityName,
    string Name,
    ShippingRateType RateType,
    decimal Amount,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    decimal? MinOrderAmount,
    int Priority);

/// <summary>
/// A delivery blackout window for a shipping method.
/// </summary>
public sealed record DeliveryBlackoutDto(
    Guid Id,
    Guid ShippingMethodId,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    string? Reason,
    bool IsActive);

public sealed record CreateDeliveryBlackoutRequest(
    Guid ShippingMethodId,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    string? Reason);

public sealed record UpdateDeliveryBlackoutRequest(
    Guid Id,
    Guid ShippingMethodId,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    string? Reason,
    bool IsActive);

/// <summary>
/// Activation toggle shared by shipping methods and zones.
/// </summary>
public sealed record ToggleShippingRequest(bool IsActive);

// ---------------------------------------------------------------------------
// Customer-facing quote inputs and results. Prices are always computed on the
// server; the client only supplies identifiers, an address and a subtotal, and
// even the subtotal is only used to evaluate free-shipping thresholds.
// ---------------------------------------------------------------------------

/// <summary>
/// A single cart line used by the shipping engine. The engine loads the live
/// product category and weight itself so the caller can never influence pricing.
/// </summary>
public sealed record ShippingLineInput(Guid ProductId, Guid VariantId, int Quantity);

/// <summary>
/// Client-supplied quote request from the cart page. Only the free-form destination
/// is accepted; the cart lines and subtotal are resolved server-side so the browser
/// can never influence pricing.
/// </summary>
public sealed record ShippingQuoteRequest(
    string CountryCode,
    string? City,
    string? Region,
    string? PostalCode);

/// <summary>
/// Input for a server-side shipping quote. The destination is free-form so guests
/// can quote without an account; the engine validates country and city support.
/// </summary>
public sealed record ShippingCalculationInput(
    string CountryCode,
    string? City,
    string? Region,
    string? PostalCode,
    decimal Subtotal,
    IReadOnlyList<ShippingLineInput> Lines,
    bool CouponFreeShipping = false);

/// <summary>
/// A single delivery option returned to the customer with the server-computed
/// price, estimated window, availability and an optional free-shipping progress.
/// </summary>
public sealed record ShippingQuoteDto(
    Guid MethodId,
    string Code,
    string Name,
    string? Description,
    ShippingMethodType Type,
    decimal Price,
    bool IsFree,
    bool IsAvailable,
    string? UnavailableReason,
    int EstimatedMinDays,
    int EstimatedMaxDays,
    bool SupportsCashOnDelivery,
    decimal? FreeShippingThreshold,
    decimal? RemainingForFreeShipping,
    string? PickupInstructions);

/// <summary>
/// Result of a shipping quote. <see cref="IsSupported"/> is false when the
/// destination is not recognized or is not served by any method; the individual
/// quotes still carry per-method availability so the UI can show disabled reasons.
/// </summary>
public sealed record ShippingQuoteResultDto(
    bool IsSupported,
    string? UnsupportedReason,
    IReadOnlyList<ShippingQuoteDto> Quotes);
