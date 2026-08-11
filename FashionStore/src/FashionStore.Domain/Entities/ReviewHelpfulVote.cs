using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A "helpful" vote cast by a customer on a review. The unique
/// (ReviewId, UserId) pair ensures a customer can only vote once per review;
/// the aggregate <see cref="ProductReview.HelpfulCount"/> is derived from these rows.
/// </summary>
public class ReviewHelpfulVote : Entity
{
    public Guid ReviewId { get; set; }
    public virtual ProductReview? Review { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
