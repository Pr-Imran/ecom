using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

#pragma warning disable CA1711

public class Category : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public Guid? ParentCategoryId { get; set; }
    public virtual Category? ParentCategory { get; set; }
    public virtual ICollection<Category> Children { get; set; } = new List<Category>();

    public int DisplayOrder { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(500)]
    public string? IconUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public bool ShowInMainMenu { get; set; }

    [MaxLength(200)]
    public string? SeoTitle { get; set; }

    [MaxLength(2000)]
    public string? SeoDescription { get; set; }
}

public class Brand : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    [MaxLength(200)]
    public string? SeoTitle { get; set; }

    [MaxLength(2000)]
    public string? SeoDescription { get; set; }
}

public class Collection : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? BannerImageUrl { get; set; }

    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    [MaxLength(200)]
    public string? SeoTitle { get; set; }

    [MaxLength(2000)]
    public string? SeoDescription { get; set; }
}

