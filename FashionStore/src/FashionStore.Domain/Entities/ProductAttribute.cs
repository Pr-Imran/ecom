using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

#pragma warning disable CA1711
public class ProductAttribute : AuditedEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string DisplayType { get; set; } = "Dropdown";

    public bool IsVariationAttribute { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public virtual ICollection<ProductAttributeValue> Values { get; set; } = new List<ProductAttributeValue>();
}

public class ProductAttributeValue : AuditedEntity
{
    public Guid ProductAttributeId { get; set; }
    public virtual ProductAttribute? ProductAttribute { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? DisplayValue { get; set; }

    [MaxLength(20)]
    public string? HexColour { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [Timestamp]
    public uint[] RowVersion { get; set; } = Array.Empty<uint>();

    public virtual ICollection<ProductVariantAttributeValue> VariantAttributeValues { get; set; } = new List<ProductVariantAttributeValue>();
}

public class ProductVariant : AuditedEntity
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    [Required]
    [MaxLength(100)]
    public string Sku { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Barcode { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? CompareAtPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? CostPrice { get; set; }

    [Column(TypeName = "decimal(10,3)")]
    public decimal? Weight { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public int? StockQuantity { get; set; }

    public int? ReservedStock { get; set; }

    public int? LowStockThreshold { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    [Timestamp]
    public uint[] RowVersion { get; set; } = Array.Empty<uint>();

    public virtual ICollection<ProductVariantAttributeValue> VariantAttributeValues { get; set; } = new List<ProductVariantAttributeValue>();
}

public class ProductVariantAttributeValue
{
    public Guid ProductVariantId { get; set; }
    public virtual ProductVariant? Variant { get; set; }

    public Guid ProductAttributeValueId { get; set; }
    public virtual ProductAttributeValue? AttributeValue { get; set; }
}
#pragma warning restore CA1711
