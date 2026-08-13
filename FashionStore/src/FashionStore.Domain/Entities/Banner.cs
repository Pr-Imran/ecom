using System.ComponentModel.DataAnnotations;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A promotional banner shown on the storefront. Homepage banners appear in the
/// promo rail; announcement banners appear at the top of every page. A banner is
/// only visible when it is Published and today is inside its optional
/// start/end window.
/// </summary>
public class Banner : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(500)]
    public string? Subtitle { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    [MaxLength(100)]
    public string? LinkText { get; set; }

    /// <summary>Visual style key used by the renderer (primary, dark, accent...).</summary>
    [MaxLength(50)]
    public string Style { get; set; } = "primary";

    public BannerPlacement Placement { get; set; } = BannerPlacement.Homepage;

    public int DisplayOrder { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
}
