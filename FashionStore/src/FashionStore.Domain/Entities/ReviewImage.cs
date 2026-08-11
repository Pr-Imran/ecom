using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A photo a customer uploaded with a review. Files are stored through the configured
/// file storage provider; only metadata is kept here.
/// </summary>
public class ReviewImage : Entity
{
    public Guid ReviewId { get; set; }
    public virtual ProductReview? Review { get; set; }

    /// <summary>Stored file name (unique within the review uploads folder).</summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? OriginalFileName { get; set; }

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Relative storage path used by the file storage provider.</summary>
    [Required]
    [MaxLength(500)]
    public string StoragePath { get; set; } = string.Empty;

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
