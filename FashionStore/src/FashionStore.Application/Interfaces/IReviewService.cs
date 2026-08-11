using FashionStore.Application.DTOs.Reviews;

namespace FashionStore.Application.Interfaces;

/// <summary>A photo file to attach to a review.</summary>
public sealed record ReviewImageInput(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);

/// <summary>
/// The customer review panel. Submission is restricted to authenticated customers,
/// and the review is only marked as a verified purchase after the system confirms a
/// delivered order line for the reviewed product owned by the caller. One review per
/// product per customer is enforced; content is sanitized server-side and screened by
/// a spam/unsafe filter before it enters the moderation queue. Rating aggregates on
/// the product are recomputed after every moderation change.
/// </summary>
public interface IReviewService
{
    /// <summary>Rating summary (average + distribution) for a product, approved reviews only.</summary>
    Task<ProductRatingSummaryDto> GetRatingSummaryAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    /// <summary>Eligibility + duplicate status for the signed-in customer on a product.</summary>
    Task<ReviewEligibilityDto> GetEligibilityAsync(
        string userId,
        Guid productId,
        CancellationToken cancellationToken = default);

    /// <summary>Paged public review list with sort and filter options.</summary>
    Task<ReviewListResultDto> GetReviewsAsync(
        Guid productId,
        ReviewQueryRequest query,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>Submits a new review after server-side rule validation.</summary>
    Task<ReviewSubmissionResult> SubmitAsync(
        string userId,
        string? displayName,
        ReviewSubmissionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads photos for a review the caller wrote, before moderation closes it.</summary>
    Task<ReviewMutationResult> UploadImagesAsync(
        string userId,
        Guid reviewId,
        IReadOnlyList<ReviewImageInput> files,
        CancellationToken cancellationToken = default);

    /// <summary>Toggles the caller's "helpful" vote on an approved review.</summary>
    Task<ReviewHelpfulResult> ToggleHelpfulAsync(
        string userId,
        Guid reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>Paged list of the caller's own reviews.</summary>
    Task<MyReviewsResultDto> GetMyReviewsAsync(
        string userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a storefront product slug to the identity used by the review screens.</summary>
    Task<ReviewProductDto?> GetReviewableProductAsync(
        string slug,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a review mutation (image upload, moderation change).</summary>
public sealed record ReviewMutationResult(bool Success, Guid? ReviewId, string? ErrorMessage);
