using System.ComponentModel.DataAnnotations;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A configurable section on the storefront homepage. Each section carries a
/// stable key (hero, promo, benefits, lookbook, newsletter, custom) and either
/// structured content in <see cref="ContentJson"/> or free-form HTML for custom
/// sections. Only published sections inside their schedule window are rendered,
/// ordered by <see cref="DisplayOrder"/>.
/// </summary>
public class HomepageSection : AuditedEntity
{
    /// <summary>The section type key (hero, promo, benefits, lookbook, newsletter, custom).</summary>
    [Required]
    [MaxLength(50)]
    public string SectionType { get; set; } = "custom";

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Subtitle { get; set; }

    /// <summary>Structured section content (banners, benefits, lookbook) as JSON.</summary>
    public string? ContentJson { get; set; }

    /// <summary>Free-form rich HTML used by custom sections.</summary>
    public string? Html { get; set; }

    public int DisplayOrder { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
}
