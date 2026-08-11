using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A photo the customer uploaded as evidence for a return request. Files are stored
/// through the configured file storage provider; only metadata is kept here.
/// </summary>
public class ReturnAttachment : Entity
{
    public Guid ReturnRequestId { get; set; }
    public virtual ReturnRequest? ReturnRequest { get; set; }

    /// <summary>Stored file name (unique within the return uploads folder).</summary>
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

    [MaxLength(450)]
    public string? UploadedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
