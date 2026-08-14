using System.ComponentModel.DataAnnotations;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// Permanent (301) redirect for a changed catalogue or content slug. When a
/// product, category, brand, collection or page slug is renamed the old slug is
/// recorded here so deep links and search indexes keep working. The public
/// controllers resolve the redirect when the exact slug no longer matches.
/// </summary>
public sealed class SlugRedirect : AuditedEntity
{
    /// <summary>The kind of catalogue/content entity the redirect applies to.</summary>
    public SlugEntityType EntityType { get; set; }

    /// <summary>The old slug that should redirect (case-insensitive match).</summary>
    [Required]
    [MaxLength(200)]
    public string OldSlug { get; set; } = string.Empty;

    /// <summary>The slug to redirect to. Empty means the entity no longer exists (410).</summary>
    [Required]
    [MaxLength(200)]
    public string NewSlug { get; set; } = string.Empty;
}
