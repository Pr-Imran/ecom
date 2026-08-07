using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A shipping zone groups countries (and optionally cities) that share delivery
/// rules. Rates reference a zone for region-based pricing and city-based pricing
/// overrides. A zone with no cities applies to the whole country; when cities are
/// configured only those cities belong to the zone.
/// </summary>
public class ShippingZone : Entity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual ICollection<ShippingZoneCountry> Countries { get; set; } = new List<ShippingZoneCountry>();

    public virtual ICollection<ShippingZoneCity> Cities { get; set; } = new List<ShippingZoneCity>();
}

/// <summary>
/// ISO 3166-1 alpha-2 country membership of a shipping zone.
/// </summary>
public class ShippingZoneCountry : Entity
{
    public Guid ShippingZoneId { get; set; }

    public virtual ShippingZone? ShippingZone { get; set; }

    [Required]
    [MaxLength(2)]
    public string CountryCode { get; set; } = string.Empty;
}

/// <summary>
/// Optional city membership of a shipping zone. The normalized name is stored so
/// matching stays case-insensitive and whitespace-tolerant.
/// </summary>
public class ShippingZoneCity : Entity
{
    public Guid ShippingZoneId { get; set; }

    public virtual ShippingZone? ShippingZone { get; set; }

    [Required]
    [MaxLength(100)]
    public string CityName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string NormalizedCityName { get; set; } = string.Empty;
}
