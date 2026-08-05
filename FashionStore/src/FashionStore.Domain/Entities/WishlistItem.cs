using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A product (and optionally a specific variant) saved by a customer for later.
/// A null <see cref="ProductVariantId"/> means the item was saved at product level
/// without a variation selection.
/// </summary>
public class WishlistItem : Entity
{
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    public Guid? ProductVariantId { get; set; }
    public virtual ProductVariant? Variant { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
