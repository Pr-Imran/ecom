using System.ComponentModel.DataAnnotations;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A legal / informational policy document such as the delivery policy, return
/// policy, privacy policy or terms of service. Documents are keyed by a stable
/// code that the storefront routes map to.
/// </summary>
public class PolicyDocument : AuditedEntity
{
    /// <summary>Stable route code (delivery-policy, return-policy, privacy-policy, terms).</summary>
    [Required]
    [MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Summary { get; set; }

    /// <summary>Sanitized rich body content.</summary>
    public string? BodyHtml { get; set; }

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTime? PublishedAtUtc { get; set; }
}
