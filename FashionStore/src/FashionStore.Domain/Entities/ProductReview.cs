using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

public class ProductReview : AuditedEntity
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(4000)]
    public string? Body { get; set; }

    public bool IsApproved { get; set; }
    public bool IsVerifiedPurchase { get; set; }
}
