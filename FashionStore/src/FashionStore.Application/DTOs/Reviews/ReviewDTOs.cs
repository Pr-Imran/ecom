using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Reviews;

/// <summary>Rating distribution entry for the star-breakdown bar chart.</summary>
public sealed record RatingDistributionDto(int Star, int Count);

/// <summary>Aggregated rating summary for a product (approved reviews only).</summary>
public sealed record ProductRatingSummaryDto(
    decimal? AverageRating,
    int ReviewCount,
    IReadOnlyList<RatingDistributionDto> Distribution);

/// <summary>A public review image.</summary>
public sealed record ReviewImageDto(Guid Id, string Url, string ContentType);

/// <summary>
/// The write-review context for a product: whether the signed-in customer is
/// eligible to review (delivered purchase) and whether they have already reviewed it.
/// </summary>
public sealed record ReviewEligibilityDto(
    bool IsEligible,
    bool AlreadyReviewed,
    Guid? ExistingReviewId,
    string? ErrorMessage);

/// <summary>
/// Review submission payload. The browser supplies the rating, title, body and the
/// optional order line the review refers to; the service re-validates ownership of
/// that line, the delivered-purchase rule and the duplicate-review rule.
/// </summary>
public sealed record ReviewSubmissionRequest(
    Guid ProductId,
    int Rating,
    string? Title,
    string? Body,
    Guid? OrderItemId);

public sealed record ReviewSubmissionResult(
    bool Success,
    Guid? ReviewId,
    string? Status,
    bool IsFlagged,
    string? ErrorMessage);

public sealed record ReviewQueryRequest(
    int Page = 1,
    int PageSize = 10,
    string? Sort = "recent",
    int? Rating = null,
    bool? HasPhotos = null);

public sealed record ReviewListItemDto(
    Guid ReviewId,
    int Rating,
    string? Title,
    string? Body,
    string DisplayName,
    bool IsVerifiedPurchase,
    int HelpfulCount,
    DateTime CreatedAtUtc,
    IReadOnlyList<ReviewImageDto> Images,
    bool IsHelpful);

public sealed record ReviewListResultDto(
    IReadOnlyList<ReviewListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore,
    ProductRatingSummaryDto Summary);

public sealed record ReviewHelpfulResult(bool Success, bool Voted, int HelpfulCount, string? ErrorMessage);

public sealed record MyReviewListItemDto(
    Guid ReviewId,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    int Rating,
    string? Title,
    string Status,
    int HelpfulCount,
    bool IsVerifiedPurchase,
    DateTime CreatedAtUtc);

public sealed record MyReviewsResultDto(
    IReadOnlyList<MyReviewListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore);

// ---- Administrative DTOs ----

public sealed record AdminReviewQueryRequest(
    int Page = 1,
    int PageSize = 20,
    ReviewStatus? Status = null,
    int? Rating = null,
    bool? VerifiedOnly = null,
    bool? FlaggedOnly = null,
    string? Search = null);

public sealed record AdminReviewListItemDto(
    Guid ReviewId,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    int Rating,
    string? Title,
    string Status,
    bool IsVerifiedPurchase,
    bool IsFlagged,
    int HelpfulCount,
    string CustomerDisplayName,
    DateTime CreatedAtUtc);

public sealed record AdminReviewListResultDto(
    IReadOnlyList<AdminReviewListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore);

public sealed record AdminReviewDetailDto(
    Guid ReviewId,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    int Rating,
    string? Title,
    string? Body,
    string DisplayName,
    string UserId,
    bool IsVerifiedPurchase,
    bool IsFlagged,
    int HelpfulCount,
    string Status,
    string? ModerationNotes,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string? OrderNumber,
    string? OrderItemName,
    IReadOnlyList<ReviewImageDto> Images);

/// <summary>Approved, Rejected or Hidden plus optional moderation notes.</summary>
public sealed record ModerateReviewRequest(ReviewStatus Status, string? Notes);

public sealed record AdminReviewMutationResult(bool Success, Guid? ReviewId, string? Status, string? ErrorMessage);

/// <summary>Public identity of a product surfaced by its storefront slug for the review screens.</summary>
public sealed record ReviewProductDto(
    Guid ProductId,
    string Name,
    string Slug,
    bool AllowReviews,
    bool IsActive);
