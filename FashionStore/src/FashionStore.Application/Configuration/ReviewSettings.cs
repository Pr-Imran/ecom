namespace FashionStore.Application.Configuration;

/// <summary>
/// Review submission and moderation rules. All limits are enforced server-side; the
/// browser never supplies them. A review may only be written for a product the
/// customer actually purchased in a delivered order, and one review per product is
/// enforced per the documented duplicate rule.
/// </summary>
public sealed class ReviewSettings
{
    public const string SectionName = "Reviews";

    /// <summary>Minimum allowed rating.</summary>
    public int MinRating { get; init; } = 1;

    /// <summary>Maximum allowed rating.</summary>
    public int MaxRating { get; init; } = 5;

    /// <summary>Maximum number of images a customer may attach to one review.</summary>
    public int MaxImagesPerReview { get; init; } = 6;

    /// <summary>Maximum size in bytes of a single review photo.</summary>
    public long MaxImageBytes { get; init; } = 5242880;

    /// <summary>Allowed review photo extensions.</summary>
    public string[] AllowedImageExtensions { get; init; } = { ".jpg", ".jpeg", ".png", ".webp" };

    /// <summary>Minimum review body length (excluding whitespace).</summary>
    public int MinBodyLength { get; init; } = 10;

    /// <summary>Maximum review body length.</summary>
    public int MaxBodyLength { get; init; } = 4000;

    /// <summary>Maximum title length.</summary>
    public int MaxTitleLength { get; init; } = 200;

    /// <summary>
    /// Whether a review becomes visible immediately. When false, every review starts
    /// Pending and must be approved by a moderator.
    /// </summary>
    public bool AutoApproveReviews { get; init; }

    /// <summary>Whether reviews of inactive (unpublished) products may be submitted.</summary>
    public bool AllowReviewsForInactiveProducts { get; init; }
}
