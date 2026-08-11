using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single returned line on a <see cref="ReturnRequest"/>. Every catalogue field is
/// copied from the order item snapshot at request time so the line stays fully
/// readable after the original product or variant is renamed or removed. The
/// <see cref="RefundableAmount"/> is computed server-side from the order snapshot and
/// is never supplied by the browser.
/// </summary>
public class ReturnItem : Entity
{
    public Guid ReturnRequestId { get; set; }
    public virtual ReturnRequest? ReturnRequest { get; set; }

    /// <summary>The order line this return references.</summary>
    public Guid OrderItemId { get; set; }

    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Sku { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ColourName { get; set; }

    [MaxLength(50)]
    public string? ColourValue { get; set; }

    [MaxLength(100)]
    public string? SizeName { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    /// <summary>Promotional discount attributed to the line at placement time.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Tax { get; set; }

    /// <summary>Quantity being returned for this line.</summary>
    public int Quantity { get; set; }

    /// <summary>Total quantity purchased for this line (used to cap returns).</summary>
    public int PurchasedQuantity { get; set; }

    /// <summary>Money refundable for this returned quantity, computed at request time.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundableAmount { get; set; }

    /// <summary>Condition recorded during inspection; drives the restock decision.</summary>
    public ReturnItemCondition Condition { get; set; } = ReturnItemCondition.Undetermined;

    /// <summary>True when a sellable returned item was added back to sellable stock.</summary>
    public bool IsRestocked { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? RestockedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? InspectedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? InspectionNote { get; set; }
}
