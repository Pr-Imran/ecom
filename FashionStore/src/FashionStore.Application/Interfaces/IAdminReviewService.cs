using FashionStore.Application.DTOs.Reviews;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Administrative review moderation. Every moderation decision (approve / reject /
/// hide / delete / notes) is guarded by the <c>Reviews.Manage</c> permission, records
/// a moderation note when supplied, and recomputes the product rating aggregate
/// inside the same transaction so the storefront summary never goes stale.
/// </summary>
public interface IAdminReviewService
{
    /// <summary>Paged moderation queue with status / rating / verified / flagged / search filters.</summary>
    Task<AdminReviewListResultDto> GetReviewsAsync(
        AdminReviewQueryRequest query,
        CancellationToken cancellationToken = default);

    /// <summary>Full detail for one review including customer, product and moderation notes.</summary>
    Task<AdminReviewDetailDto?> GetReviewDetailAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>Applies an approve / reject / hide decision with optional notes.</summary>
    Task<AdminReviewMutationResult> ModerateAsync(
        Guid reviewId,
        ModerateReviewRequest request,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a review where policy permits (pending, rejected or flagged).</summary>
    Task<AdminReviewMutationResult> DeleteAsync(
        Guid reviewId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Appends an internal moderation note without changing the status.</summary>
    Task<AdminReviewMutationResult> AddNoteAsync(
        Guid reviewId,
        string note,
        string actorId,
        CancellationToken cancellationToken = default);
}
