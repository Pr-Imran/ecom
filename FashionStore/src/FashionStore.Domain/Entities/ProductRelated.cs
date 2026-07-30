using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

public class ProductTag : AuditedEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public virtual ICollection<ProductTagMapping> ProductTagMappings { get; set; } = new List<ProductTagMapping>();
}

public class ProductTagMapping
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    public Guid ProductTagId { get; set; }
    public virtual ProductTag? ProductTag { get; set; }
}

public class RelatedProduct
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    public Guid RelatedProductId { get; set; }
    public virtual Product? RelatedProductEntity { get; set; }

    public int DisplayOrder { get; set; }
}

public class ProductSpecification : AuditedEntity
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}

public class ProductSizeGuideMapping
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    public Guid SizeGuideId { get; set; }

    public string? SizeMapping { get; set; }

    public int DisplayOrder { get; set; }
}
