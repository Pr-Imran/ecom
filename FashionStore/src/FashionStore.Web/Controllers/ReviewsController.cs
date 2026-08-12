using System.Security.Claims;
using FashionStore.Application.DTOs.Reviews;
using FashionStore.Application.Interfaces;
using FashionStore.Application.Services;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Controllers;

/// <summary>
/// The customer-facing review surface. The public per-product page
/// (<c>/products/{slug}/reviews</c>) shows the rating summary, star distribution,
/// sortable / filterable list, verified-purchase badges, helpful votes and photo
/// galleries. Writing a review requires a signed-in customer, and the service
/// re-validates eligibility (delivered purchase, duplicate rule) and sanitizes
/// content server-side before anything is persisted; photo uploads and helpful
/// votes are ownership-checked by the same service.
/// </summary>
public class ReviewsController : Controller
{
    private readonly IReviewService _reviewService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(
        IReviewService reviewService,
        INavigationService navigationService,
        ILogger<ReviewsController> logger)
    {
        _reviewService = reviewService;
        _navigationService = navigationService;
        _logger = logger;
    }

    /// <summary>
    /// Public, SEO-rendered reviews page for a product. Reviews shown are approved
    /// only; sort and filter options are re-applied server-side on every request.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("products/{slug}/reviews")]
    public async Task<IActionResult> Index(
        string slug,
        [FromQuery] ReviewQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var product = await _reviewService.GetReviewableProductAsync(slug, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviews = await _reviewService.GetReviewsAsync(product.ProductId, query, userId, cancellationToken);

        ReviewEligibilityDto? eligibility = null;
        if (!string.IsNullOrEmpty(userId))
        {
            eligibility = await _reviewService.GetEligibilityAsync(userId, product.ProductId, cancellationToken);
        }

        ViewData["Title"] = $"Reviews for {product.Name}";
        ViewData["CanonicalUrl"] = $"{Request.Scheme}://{Request.Host}{Request.Path}";

        return View(new ReviewPageViewModel
        {
            Product = product,
            Reviews = reviews,
            Query = query,
            IsAuthenticated = !string.IsNullOrEmpty(userId),
            Eligibility = eligibility
        });
    }

    /// <summary>
    /// Mobile full-screen write-review form. Only reachable by a signed-in customer
    /// who has a delivered purchase for the product and has not already reviewed it.
    /// </summary>
    [Authorize]
    [HttpGet("products/{slug}/reviews/write")]
    public async Task<IActionResult> Write(string slug, CancellationToken cancellationToken = default)
    {
        var product = await _reviewService.GetReviewableProductAsync(slug, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var eligibility = await _reviewService.GetEligibilityAsync(userId, product.ProductId, cancellationToken);

        if (!eligibility.IsEligible)
        {
            TempData["ReviewError"] = eligibility.ErrorMessage ?? "You cannot review this product right now.";
            return RedirectToAction(nameof(Index), new { slug });
        }

        ViewData["Title"] = "Write a Review";
        return View(new WriteReviewViewModel { Product = product, Eligibility = eligibility });
    }

    /// <summary>
    /// Submits the review. The service re-validates every rule (authenticated
    /// customer, delivered purchase, duplicate review, sanitized content, rating
    /// bounds) and photo uploads are attached to the created review afterwards.
    /// </summary>
    [Authorize]
    [HttpPost("reviews")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(SubmitReviewForm form, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var result = await _reviewService.SubmitAsync(
            userId,
            User.Identity?.Name,
            new ReviewSubmissionRequest(form.ProductId, form.Rating, form.Title, form.Body, form.OrderItemId),
            cancellationToken);

        if (!result.Success)
        {
            TempData["ReviewError"] = result.ErrorMessage;
            return RedirectToAction(nameof(Write), new { slug = form.ProductSlug });
        }

        if (form.Photos is { Count: > 0 } && result.ReviewId.HasValue)
        {
            var images = form.Photos
                .Select(f => new ReviewImageInput(
                    f.OpenReadStream(),
                    f.FileName,
                    string.IsNullOrWhiteSpace(f.ContentType) ? "image/jpeg" : f.ContentType,
                    f.Length))
                .ToList();

            var upload = await _reviewService.UploadImagesAsync(userId, result.ReviewId.Value, images, cancellationToken);
            if (!upload.Success)
            {
                _logger.LogWarning("Review {ReviewId} submitted but photo upload failed: {Error}", result.ReviewId.Value, upload.ErrorMessage);
            }
        }

        TempData["ReviewMessage"] = result.Status == "Approved"
            ? "Thanks! Your review is now live on the product page."
            : "Thanks! Your review has been submitted and is awaiting moderation.";

        return RedirectToAction(nameof(Index), new { slug = form.ProductSlug });
    }

    /// <summary>
    /// Toggles the signed-in customer's helpful vote on an approved review. JSON
    /// response so the product page can update the count without a full reload.
    /// </summary>
    [Authorize]
    [HttpPost("reviews/{id:guid}/helpful")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleHelpful(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _reviewService.ToggleHelpfulAsync(userId, id, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        return Ok(new { success = true, voted = result.Voted, helpfulCount = result.HelpfulCount });
    }

    /// <summary>
    /// Uploads photos for a review the caller wrote. JSON response used by the
    /// photo picker in the write-review flow.
    /// </summary>
    [Authorize]
    [HttpPost("reviews/{id:guid}/images")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadImages(Guid id, IFormFileCollection files, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var images = files
            .Select(f => new ReviewImageInput(
                f.OpenReadStream(),
                f.FileName,
                string.IsNullOrWhiteSpace(f.ContentType) ? "image/jpeg" : f.ContentType,
                f.Length))
            .ToList();

        var result = await _reviewService.UploadImagesAsync(userId, id, images, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// The authenticated customer's own reviews under <c>/account/reviews</c>.
    /// </summary>
    [Authorize]
    [HttpGet("account/reviews")]
    public async Task<IActionResult> MyReviews([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _reviewService.GetMyReviewsAsync(userId, page, pageSize, cancellationToken);

        ViewData["Title"] = "My Reviews";
        ViewData["AccountNav"] = await _navigationService.GetAccountNavigationAsync(userId, cancellationToken);

        return View(result);
    }
}
