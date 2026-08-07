using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single pricing entry for a shipping method. The rate is scoped by an optional
/// <see cref="ShippingZoneId"/> (region-based), an optional <see cref="CityName"/>
/// (city-based override) and an optional weight band. When neither the zone nor the
/// city is set the rate is the method's global fallback. At quote time the rate
/// whose weight band and minimum order amount match the cart is selected by
/// specificity (city then zone then global) and, within a level, by the lowest
/// priority. All values are validated and applied on the server.
/// </summary>
public class ShippingRate : Entity
{
    public Guid ShippingMethodId { get; set; }

    public virtual ShippingMethod? ShippingMethod { get; set; }

    /// <summary>Null means the rate applies to every zone (global fallback).</summary>
    public Guid? ShippingZoneId { get; set; }

    public virtual ShippingZone? ShippingZone { get; set; }

    /// <summary>City override name; matched case-insensitively against the destination.</summary>
    [MaxLength(100)]
    public string? CityName { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public ShippingRateType RateType { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>Lower bound of the weight band in kilograms (inclusive).</summary>
    [Column(TypeName = "decimal(10,3)")]
    public decimal? MinWeightKg { get; set; }

    /// <summary>Upper bound of the weight band in kilograms (inclusive).</summary>
    [Column(TypeName = "decimal(10,3)")]
    public decimal? MaxWeightKg { get; set; }

    /// <summary>When set the rate only applies to carts whose subtotal reaches this value.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? MinOrderAmount { get; set; }

    /// <summary>Lower values win when several rates match the same destination.</summary>
    public int Priority { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
