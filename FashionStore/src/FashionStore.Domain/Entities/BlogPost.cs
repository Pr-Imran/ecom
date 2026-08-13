using System.ComponentModel.DataAnnotations;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A blog post. Blog management is planned for a later phase; the entity and
/// schema are created now so content administrators can prepare posts before the
/// public blog surface ships.
/// </summary>
public class BlogPost : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Excerpt { get; set; }

    public string? ContentHtml { get; set; }

    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? PublishedAtUtc { get; set; }

    [MaxLength(200)]
    public string? AuthorName { get; set; }
}
