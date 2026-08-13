using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A named navigation menu (for example "Main" or "Footer"). Menus hold a flat
/// list of <see cref="NavigationItem"/> records; hierarchy is expressed through
/// the parent/child relationship on the items.
/// </summary>
public class NavigationMenu : AuditedEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable code used to look up the menu (for example <c>main</c>).</summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual ICollection<NavigationItem> Items { get; set; } = new List<NavigationItem>();
}
