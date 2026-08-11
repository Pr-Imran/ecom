using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A customer review of a product. Content is moderated before it becomes public:
/// reviews start Pending, a moderator approves or rejects them, and a hidden review
/// is suppressed without being deleted. The <see cref="IsVerifiedPurchase"/> flag is
/// only set after the system confirms the customer has a delivered order containing
/// the reviewed product. Rating aggregates on <see cref="Product"/> are recomputed
/// after every approve / reject / hide / delete so the summary stays consistent.
/// </summary>
public class ProductReview : AuditedEntity
{
    public Guid ProductId { get; set; }
    public virtual Product? Product { get; set; }

    /// <summary>Customer who wrote the review (null never — reviews require a signed-in user).</summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(4000)]
    public string? Body { get; set; }

    /// <summary>The order line that proved this was a purchased item, when the review
    /// was written against a delivered order item.</summary>
    public Guid? OrderItemId { get; set; }

    /// <summary>The delivered order used to verify this purchase, when applicable.</summary>
    public Guid? OrderId { get; set; }

    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

    /// <summary>True once a delivered order for the reviewed product was confirmed.</summary>
    public bool IsVerifiedPurchase { get; set; }

    /// <summary>Number of "helpful" votes from other customers.</summary>
    public int HelpfulCount { get; set; }

    /// <summary>Set by the content moderator when the text looked like spam or unsafe content.</summary>
    public bool IsFlagged { get; set; }

    /// <summary>Internal moderation notes recorded by the admin team.</summary>
    [MaxLength(2000)]
    public string? ModerationNotes { get; set; }

    public virtual ICollection<ReviewImage> Images { get; set; } = new List<ReviewImage>();
    public virtual ICollection<ReviewHelpfulVote> HelpfulVotes { get; set; } = new List<ReviewHelpfulVote>();
}
