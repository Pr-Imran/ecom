using System.Security.Claims;
using FashionStore.Application.Authorization;
using FashionStore.Application.DTOs.Reviews;
using FashionStore.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers.Admin;

/// <summary>
/// Administrative review moderation API. Every moderation decision (approve /
/// reject / hide / delete / notes) is guarded by the <c>Reviews.Manage</c> policy
/// and flows through <see cref="IAdminReviewService"/>, which recomputes the
/// product rating aggregate in the same transaction as the change.
/// </summary>
[ApiController]
[Route("api/admin/reviews")]
public class AdminReviewsController : ControllerBase
{
    private readonly IAdminReviewService _reviewService;
    private readonly ILogger<AdminReviewsController> _logger;

    public AdminReviewsController(
        IAdminReviewService reviewService,
        ILogger<AdminReviewsController> logger)
    {
        _reviewService = reviewService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = ReviewPolicies.ReviewsManage)]
    public async Task<IActionResult> GetReviews(
        [FromQuery] AdminReviewQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _reviewService.GetReviewsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = ReviewPolicies.ReviewsManage)]
    public async Task<IActionResult> GetReview(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await _reviewService.GetReviewDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>Applies an approve / reject / hide decision with optional moderation notes.</summary>
    [HttpPost("{id:guid}/moderate")]
    [Authorize(Policy = ReviewPolicies.ReviewsManage)]
    public async Task<IActionResult> Moderate(Guid id, [FromBody] ModerateReviewRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, error = "A moderation decision is required." });
        }

        return await RunMutationAsync(
            () => _reviewService.ModerateAsync(id, request, ActorId(), cancellationToken));
    }

    /// <summary>Deletes a review where policy permits (pending, rejected, hidden or flagged).</summary>
    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = ReviewPolicies.ReviewsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await RunMutationAsync(
            () => _reviewService.DeleteAsync(id, ActorId(), cancellationToken));
    }

    /// <summary>Appends an internal moderation note without changing the status.</summary>
    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = ReviewPolicies.ReviewsManage)]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddReviewNoteRequest? request, CancellationToken cancellationToken = default)
    {
        return await RunMutationAsync(
            () => _reviewService.AddNoteAsync(id, request?.Note ?? string.Empty, ActorId(), cancellationToken));
    }

    private async Task<IActionResult> RunMutationAsync(Func<Task<AdminReviewMutationResult>> action)
    {
        var result = await action();

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.ErrorMessage, reviewId = result.ReviewId });
        }

        return Ok(new { success = true, reviewId = result.ReviewId, status = result.Status });
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
}

/// <summary>Payload for appending a moderation note.</summary>
public sealed record AddReviewNoteRequest(string? Note);
