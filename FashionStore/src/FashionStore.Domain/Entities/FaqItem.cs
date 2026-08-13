using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A frequently asked question shown on the storefront FAQ page. Items are
/// grouped by an optional category and ordered by <see cref="DisplayOrder"/>.
/// </summary>
public class FaqItem : AuditedEntity
{
    [Required]
    [MaxLength(500)]
    public string Question { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Answer { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
