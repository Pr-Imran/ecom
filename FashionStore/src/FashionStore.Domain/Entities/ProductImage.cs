using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

public class ProductImage : AuditedEntity
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    public Guid? ProductVariantId { get; set; }
    public virtual ProductVariant? Variant { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? OriginalFileName { get; set; }

    [MaxLength(500)]
    public string? AltText { get; set; }

    [MaxLength(500)]
    public string? Caption { get; set; }

    public bool IsMain { get; set; }

    public int DisplayOrder { get; set; }

    [Required]
    [MaxLength(20)]
    public string ImageFormat { get; set; } = "jpeg";

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = "image/jpeg";

    public long SizeBytes { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    [Required]
    [MaxLength(20)]
    public string ProcessingStatus { get; set; } = "Pending";

    [Timestamp]
    public uint[] RowVersion { get; set; } = Array.Empty<uint>();
}
