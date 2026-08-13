using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single navigation link within a <see cref="NavigationMenu"/>. Items form a
/// parent/child hierarchy through <see cref="ParentId"/>; sibling ordering is
/// controlled by <see cref="DisplayOrder"/>.
/// </summary>
public class NavigationItem : AuditedEntity
{
    public Guid MenuId { get; set; }
    public virtual NavigationMenu? Menu { get; set; }

    public Guid? ParentId { get; set; }
    public virtual NavigationItem? Parent { get; set; }
    public virtual ICollection<NavigationItem> Children { get; set; } = new List<NavigationItem>();

    [Required]
    [MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? Target { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
