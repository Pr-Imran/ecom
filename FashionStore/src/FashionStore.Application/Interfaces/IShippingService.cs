using FashionStore.Application.DTOs.Shipping;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Administrative shipping configuration: shipping methods, shipping zones,
/// shipping rates and delivery blackout windows. Every write validates its input
/// server-side and throws <see cref="InvalidOperationException"/> when a rule is
/// violated; codes are normalized to upper case so they stay unique and
/// case-insensitive.
/// </summary>
public interface IShippingService
{
    // ---- Shipping methods ----

    Task<IReadOnlyList<ShippingMethodDto>> GetMethodsAsync(
        bool includeInactive = true,
        CancellationToken cancellationToken = default);

    Task<ShippingMethodDto?> GetMethodByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ShippingMethodDto> CreateMethodAsync(CreateShippingMethodRequest request, CancellationToken cancellationToken = default);

    Task<ShippingMethodDto?> UpdateMethodAsync(Guid id, UpdateShippingMethodRequest request, CancellationToken cancellationToken = default);

    Task<bool> SetMethodActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<bool> ReorderMethodsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);

    Task<bool> IsMethodCodeUniqueAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    // ---- Shipping zones ----

    Task<IReadOnlyList<ShippingZoneDto>> GetZonesAsync(
        bool includeInactive = true,
        CancellationToken cancellationToken = default);

    Task<ShippingZoneDto?> GetZoneByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ShippingZoneDto> CreateZoneAsync(CreateShippingZoneRequest request, CancellationToken cancellationToken = default);

    Task<ShippingZoneDto?> UpdateZoneAsync(Guid id, UpdateShippingZoneRequest request, CancellationToken cancellationToken = default);

    Task<bool> SetZoneActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<bool> DeleteZoneAsync(Guid id, CancellationToken cancellationToken = default);

    // ---- Shipping rates ----

    Task<IReadOnlyList<ShippingRateDto>> GetRatesAsync(
        Guid? methodId = null,
        CancellationToken cancellationToken = default);

    Task<ShippingRateDto> CreateRateAsync(CreateShippingRateRequest request, CancellationToken cancellationToken = default);

    Task<ShippingRateDto?> UpdateRateAsync(Guid id, UpdateShippingRateRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteRateAsync(Guid id, CancellationToken cancellationToken = default);

    // ---- Delivery blackouts ----

    Task<IReadOnlyList<DeliveryBlackoutDto>> GetBlackoutsAsync(Guid methodId, CancellationToken cancellationToken = default);

    Task<DeliveryBlackoutDto> CreateBlackoutAsync(CreateDeliveryBlackoutRequest request, CancellationToken cancellationToken = default);

    Task<DeliveryBlackoutDto?> UpdateBlackoutAsync(Guid id, UpdateDeliveryBlackoutRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteBlackoutAsync(Guid id, CancellationToken cancellationToken = default);
}
