using System.ComponentModel.DataAnnotations;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A storefront content page. Custom pages are created and managed by content
/// administrators; system pages (About, Contact, Size Guide) are seeded with
/// stable slugs and can be edited but not deleted.
/// </summary>
public class ContentPage : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>URL-friendly unique identifier used in the public route.</summary>
    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Short description used in listings and search snippets.</summary>
    [MaxLength(500)]
    public string? Summary { get; set; }

    /// <summary>Sanitized rich body content.</summary>
    public string? BodyHtml { get; set; }

    public ContentPageTemplate Template { get; set; } = ContentPageTemplate.Default;

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    /// <summary>When true the page is a system page that cannot be deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>When the page became visible on the storefront (null while draft).</summary>
    public DateTime? PublishedAtUtc { get; set; }

    [MaxLength(200)]
    public string? MetaTitle { get; set; }

    [MaxLength(500)]
    public string? MetaDescription { get; set; }
}
