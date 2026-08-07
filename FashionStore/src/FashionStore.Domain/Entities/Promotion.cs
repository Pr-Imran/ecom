using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// An automatic price promotion applied to eligible cart lines. A promotion can be
/// scoped to a single product, category, brand or collection, and can require a
/// minimum quantity before it takes effect. When promotions overlap, the one with
/// the highest priority wins first and stackable promotions are combined in a
/// deterministic order.
/// </summary>
public class Promotion : Entity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public DiscountType DiscountType { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MaxDiscountAmount { get; set; }

    public int MinQuantity { get; set; } = 1;

    public int Priority { get; set; }

    public bool IsStackable { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartAtUtc { get; set; }

    public DateTime? EndAtUtc { get; set; }

    public Guid? ProductId { get; set; }
    public virtual Product? Product { get; set; }

    public Guid? CategoryId { get; set; }
    public virtual Category? Category { get; set; }

    public Guid? BrandId { get; set; }
    public virtual Brand? Brand { get; set; }

    public Guid? CollectionId { get; set; }
    public virtual Collection? Collection { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
