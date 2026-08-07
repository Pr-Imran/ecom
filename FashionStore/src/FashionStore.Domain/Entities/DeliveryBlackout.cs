using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A period during which a shipping method is unavailable (for example a courier
/// blackout over a public holiday). The window is compared against the current UTC
/// time at quote time; inactive windows are ignored.
/// </summary>
public class DeliveryBlackout : Entity
{
    public Guid ShippingMethodId { get; set; }

    public virtual ShippingMethod? ShippingMethod { get; set; }

    public DateTime StartAtUtc { get; set; }

    public DateTime EndAtUtc { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
