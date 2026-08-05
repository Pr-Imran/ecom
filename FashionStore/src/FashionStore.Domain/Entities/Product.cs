using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

public class Product : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ShortDescription { get; set; }

    public string? FullDescription { get; set; }

    public Guid CategoryId { get; set; }
    public virtual Category? Category { get; set; }

    public Guid? BrandId { get; set; }
    public virtual Brand? Brand { get; set; }

    public Guid? CollectionId { get; set; }
    public virtual Collection? Collection { get; set; }

    [Required]
    [MaxLength(50)]
    public string ProductType { get; set; } = "Standard";

    [MaxLength(100)]
    public string? Material { get; set; }

    [MaxLength(100)]
    public string? Fabric { get; set; }

    [MaxLength(500)]
    public string? CareInstructions { get; set; }

    [MaxLength(50)]
    public string? Gender { get; set; }

    [MaxLength(100)]
    public string? CountryOfOrigin { get; set; }

    [Required]
    [MaxLength(100)]
    public string BaseSku { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Barcode { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal BasePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? CompareAtPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? CostPrice { get; set; }

    [Required]
    [MaxLength(50)]
    public string TaxCategory { get; set; } = "Standard";

    [Column(TypeName = "decimal(10,3)")]
    public decimal? Weight { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public bool IsNewArrival { get; set; }
    public bool IsBestSeller { get; set; }
    public bool AllowReviews { get; set; } = true;
    public int DisplayOrder { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    [MaxLength(200)]
    public string? SeoTitle { get; set; }

    [MaxLength(2000)]
    public string? SeoDescription { get; set; }

    [MaxLength(500)]
    public string? SearchKeywords { get; set; }

    [Timestamp]
    public uint[] RowVersion { get; set; } = Array.Empty<uint>();

    public virtual ICollection<ProductTagMapping> ProductTagMappings { get; set; } = new List<ProductTagMapping>();
    public virtual ICollection<RelatedProduct> RelatedProducts { get; set; } = new List<RelatedProduct>();
    public virtual ICollection<RelatedProduct> RelatedToProducts { get; set; } = new List<RelatedProduct>();
    public virtual ICollection<ProductSpecification> Specifications { get; set; } = new List<ProductSpecification>();
    public virtual ICollection<ProductSizeGuideMapping> SizeGuideMappings { get; set; } = new List<ProductSizeGuideMapping>();
    public virtual ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public virtual ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public virtual ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
}
