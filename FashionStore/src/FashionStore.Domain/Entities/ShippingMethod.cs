using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A delivery method offered to customers (standard, express, free delivery or
/// local pickup). A method carries the estimated delivery window, cash-on-delivery
/// support, an optional free-shipping threshold and an optional maximum package
/// weight. Pricing lives in <see cref="ShippingRate"/> rows that are scoped to
/// shipping zones / cities / weight bands, and availability is further restricted
/// by <see cref="ShippingMethodProduct"/> and <see cref="ShippingMethodCategory"/>
/// join rows plus <see cref="DeliveryBlackout"/> windows. The checkout always
/// recalculates the price server-side; the browser never supplies a shipping cost.
/// </summary>
public class ShippingMethod : Entity
{
    /// <summary>Stable, unique machine key such as "STANDARD" or "LOCAL_PICKUP".</summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public ShippingMethodType Type { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    /// <summary>Lower bound of the estimated delivery window in days.</summary>
    public int EstimatedMinDays { get; set; } = 3;

    /// <summary>Upper bound of the estimated delivery window in days.</summary>
    public int EstimatedMaxDays { get; set; } = 5;

    public bool SupportsCashOnDelivery { get; set; }

    /// <summary>False for local pickup methods, which never need a shipping address.</summary>
    public bool RequiresShippingAddress { get; set; } = true;

    /// <summary>When set, delivery is free for carts whose subtotal reaches this value.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? FreeShippingThreshold { get; set; }

    /// <summary>Maximum total package weight in kilograms accepted by this method.</summary>
    [Column(TypeName = "decimal(10,3)")]
    public decimal? MaxPackageWeight { get; set; }

    [MaxLength(1000)]
    public string? PickupInstructions { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual ICollection<ShippingRate> Rates { get; set; } = new List<ShippingRate>();

    /// <summary>Product scoping rows; each row is either an inclusion or exclusion.</summary>
    public virtual ICollection<ShippingMethodProduct> ProductRestrictions { get; set; } = new List<ShippingMethodProduct>();

    /// <summary>Category scoping rows; each row is either an inclusion or exclusion.</summary>
    public virtual ICollection<ShippingMethodCategory> CategoryRestrictions { get; set; } = new List<ShippingMethodCategory>();

    public virtual ICollection<DeliveryBlackout> Blackouts { get; set; } = new List<DeliveryBlackout>();
}

/// <summary>
/// Join row that scopes a shipping method to a product. When <see cref="IsExclusion"/>
/// is false the method only applies to carts that contain one of the restricted
/// products; when true the method never applies to carts containing the product.
/// </summary>
public class ShippingMethodProduct : Entity
{
    public Guid ShippingMethodId { get; set; }

    public virtual ShippingMethod? ShippingMethod { get; set; }

    public Guid ProductId { get; set; }

    public virtual Product? Product { get; set; }

    public bool IsExclusion { get; set; }
}

/// <summary>
/// Join row that scopes a shipping method to a category, mirroring the product
/// restriction semantics of <see cref="ShippingMethodProduct"/>.
/// </summary>
public class ShippingMethodCategory : Entity
{
    public Guid ShippingMethodId { get; set; }

    public virtual ShippingMethod? ShippingMethod { get; set; }

    public Guid CategoryId { get; set; }

    public virtual Category? Category { get; set; }

    public bool IsExclusion { get; set; }
}
