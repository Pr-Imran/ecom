using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single line on an order. Every catalogue field is a snapshot captured at
/// placement time, so the line stays fully readable after the original product or
/// variant is renamed, deactivated or removed. <see cref="ProductId"/> and
/// <see cref="ProductVariantId"/> are retained (when the originals still exist) for
/// later lookups but are never required for rendering.
/// </summary>
public class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public virtual Order? Order { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? ProductVariantId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string ProductSlug { get; set; } = string.Empty;

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

    [Column(TypeName = "decimal(18,2)")]
    public decimal? CompareAtPrice { get; set; }

    /// <summary>Promotional discount attributed to this line.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Tax { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }
}
