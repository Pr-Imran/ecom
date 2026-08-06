using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single variant-level line in a customer's persisted cart. The variant is
/// always required because cart pricing, stock and option names are resolved from
/// the selected variation on the server. Quantities are capped at the available
/// stock and the configured maximum on every mutation.
/// </summary>
public class CartItem : Entity
{
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    public Guid ProductVariantId { get; set; }
    public virtual ProductVariant? Variant { get; set; }

    public int Quantity { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
