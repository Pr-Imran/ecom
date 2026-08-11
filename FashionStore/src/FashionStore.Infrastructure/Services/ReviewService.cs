using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Reviews;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// The customer review panel. Submission requires an authenticated customer who owns
/// a delivered order containing the product (verified server-side — the browser never
/// supplies the verified flag), one review per product is enforced per the duplicate
/// rule, ownership violations are refused, content is sanitized to plain text and
/// screened for spam/unsafe signals before entering the moderation queue, and rating
/// aggregates are recomputed whenever the approved set changes.
/// </summary>
public sealed class ReviewService : IReviewService
{
    private const int MaxPageSize = 50;

    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IOptions<ReviewSettings> _reviewOptions;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        AppDbContext context,
        IFileStorageService fileStorage,
        IOptions<ReviewSettings> reviewOptions,
        ILogger<ReviewService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _reviewOptions = reviewOptions;
        _logger = logger;
    }

    public async Task<ProductRatingSummaryDto> GetRatingSummaryAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
        {
            return new ProductRatingSummaryDto(null, 0, BuildEmptyDistribution());
        }

        return new ProductRatingSummaryDto(
            product.AverageRating,
            product.ReviewCount,
            new[]
            {
                new RatingDistributionDto(5, product.RatingCount5),
                new RatingDistributionDto(4, product.RatingCount4),
                new RatingDistributionDto(3, product.RatingCount3),
                new RatingDistributionDto(2, product.RatingCount2),
                new RatingDistributionDto(1, product.RatingCount1)
            });
    }

    public async Task<ReviewEligibilityDto> GetEligibilityAsync(
        string userId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var settings = _reviewOptions.Value;
        var now = DateTime.UtcNow;

        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null || (!product.IsActive && !settings.AllowReviewsForInactiveProducts))
        {
            return new ReviewEligibilityDto(false, false, null, "This product is no longer available for reviews.");
        }

        if (product.PublishedAtUtc != null && product.PublishedAtUtc > now && !settings.AllowReviewsForInactiveProducts)
        {
            return new ReviewEligibilityDto(false, false, null, "This product is not yet available for reviews.");
        }

        var existing = await _context.ProductReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId, cancellationToken);

        if (existing is not null && existing.Status != ReviewStatus.Rejected)
        {
            return new ReviewEligibilityDto(false, true, existing.Id, "You have already reviewed this product.");
        }

        var hasDeliveredPurchase = await HasDeliveredPurchaseAsync(userId, productId, cancellationToken);
        if (!hasDeliveredPurchase)
        {
            return new ReviewEligibilityDto(false, false, null, "You can review products you have purchased in a delivered order.");
        }

        return new ReviewEligibilityDto(true, false, null, null);
    }

    public async Task<ReviewListResultDto> GetReviewsAsync(
        Guid productId,
        ReviewQueryRequest query,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 10 : query.PageSize, 1, MaxPageSize);

        var baseQuery = _context.ProductReviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved);

        if (query.Rating.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Rating == query.Rating.Value);
        }

        if (query.HasPhotos.HasValue)
        {
            baseQuery = query.HasPhotos.Value
                ? baseQuery.Where(r => r.Images.Count > 0)
                : baseQuery.Where(r => r.Images.Count == 0);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var sort = string.IsNullOrWhiteSpace(query.Sort) ? "recent" : query.Sort.ToLowerInvariant();
        IOrderedQueryable<ProductReview> ordered = sort switch
        {
            "helpful" => baseQuery.OrderByDescending(r => r.HelpfulCount).ThenByDescending(r => r.CreatedAtUtc),
            "highest" => baseQuery.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAtUtc),
            "lowest" => baseQuery.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAtUtc),
            _ => baseQuery.OrderByDescending(r => r.CreatedAtUtc)
        };

        var reviews = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Images)
            .ToListAsync(cancellationToken);

        var reviewIds = reviews.Select(r => r.Id).ToList();
        var votedIds = new HashSet<Guid>();
        if (!string.IsNullOrEmpty(userId))
        {
            votedIds = (await _context.ReviewHelpfulVotes
                    .AsNoTracking()
                    .Where(v => v.UserId == userId && reviewIds.Contains(v.ReviewId))
                    .Select(v => v.ReviewId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        var summary = await GetRatingSummaryAsync(productId, cancellationToken);

        var items = reviews.Select(r => new ReviewListItemDto(
            r.Id,
            r.Rating,
            r.Title,
            r.Body,
            r.DisplayName ?? "Customer",
            r.IsVerifiedPurchase,
            r.HelpfulCount,
            r.CreatedAtUtc,
            r.Images
                .OrderBy(i => i.CreatedAtUtc)
                .Select(i => new ReviewImageDto(i.Id, _fileStorage.ResolveUrl(i.StoragePath), i.ContentType))
                .ToList(),
            votedIds.Contains(r.Id))).ToList();

        return new ReviewListResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount,
            summary);
    }

    public async Task<ReviewSubmissionResult> SubmitAsync(
        string userId,
        string? displayName,
        ReviewSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = _reviewOptions.Value;
        var now = DateTime.UtcNow;

        if (request.Rating < settings.MinRating || request.Rating > settings.MaxRating)
        {
            return new ReviewSubmissionResult(false, null, null, false, $"Choose a rating between {settings.MinRating} and {settings.MaxRating}.");
        }

        var body = ReviewContentModerator.SanitizeToPlainText(request.Body);
        var title = ReviewContentModerator.SanitizeToPlainText(request.Title);

        if (body.Length < settings.MinBodyLength)
        {
            return new ReviewSubmissionResult(false, null, null, false, $"Your review needs to be at least {settings.MinBodyLength} characters.");
        }

        if (body.Length > settings.MaxBodyLength)
        {
            return new ReviewSubmissionResult(false, null, null, false, $"Your review can be at most {settings.MaxBodyLength} characters.");
        }

        if (title.Length > settings.MaxTitleLength)
        {
            return new ReviewSubmissionResult(false, null, null, false, $"The title can be at most {settings.MaxTitleLength} characters.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);

                if (product is null || (!product.IsActive && !settings.AllowReviewsForInactiveProducts))
                {
                    return new ReviewSubmissionResult(false, null, null, false, "This product is no longer available for reviews.");
                }

                if (product.PublishedAtUtc != null && product.PublishedAtUtc > now && !settings.AllowReviewsForInactiveProducts)
                {
                    return new ReviewSubmissionResult(false, null, null, false, "This product is not yet available for reviews.");
                }

                var existing = await _context.ProductReviews
                    .FirstOrDefaultAsync(r => r.ProductId == request.ProductId && r.UserId == userId, cancellationToken);

                if (existing is not null && existing.Status != ReviewStatus.Rejected)
                {
                    return new ReviewSubmissionResult(false, null, null, false, "You have already reviewed this product. Only one review per product is allowed.");
                }

                var orderItem = await ResolveVerifiedOrderItemAsync(
                    userId,
                    request.ProductId,
                    request.OrderItemId,
                    cancellationToken);

                if (orderItem is null)
                {
                    return new ReviewSubmissionResult(false, null, null, false, "You can only review products you have purchased in a delivered order.");
                }

                var status = settings.AutoApproveReviews ? ReviewStatus.Approved : ReviewStatus.Pending;
                var isFlagged = ReviewContentModerator.IsFlagged(body) || ReviewContentModerator.IsFlagged(title);

                var review = new ProductReview
                {
                    ProductId = product.Id,
                    UserId = userId,
                    DisplayName = NormalizeOptional(displayName, 200) ?? "Customer",
                    Rating = request.Rating,
                    Title = string.IsNullOrEmpty(title) ? null : title,
                    Body = body,
                    OrderItemId = orderItem.Id,
                    OrderId = orderItem.OrderId,
                    Status = status,
                    IsVerifiedPurchase = true,
                    IsFlagged = isFlagged,
                    CreatedAtUtc = now,
                    CreatedBy = userId
                };

                _context.ProductReviews.Add(review);
                await _context.SaveChangesAsync(cancellationToken);

                if (status == ReviewStatus.Approved)
                {
                    await ProductRatingAggregator.RecomputeAsync(_context, product.Id, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Review {ReviewId} submitted for product {ProductId} by {UserId} (status {Status}, verified {Verified}, flagged {Flagged})",
                    review.Id,
                    product.Id,
                    userId,
                    status,
                    true,
                    isFlagged);

                return new ReviewSubmissionResult(true, review.Id, status.ToString(), isFlagged, null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review submission failed for product {ProductId} by user {UserId}", request.ProductId, userId);
            return new ReviewSubmissionResult(false, null, null, false, "We could not submit your review. Please try again.");
        }
    }

    public async Task<ReviewMutationResult> UploadImagesAsync(
        string userId,
        Guid reviewId,
        IReadOnlyList<ReviewImageInput> files,
        CancellationToken cancellationToken = default)
    {
        var review = await _context.ProductReviews
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null || !string.Equals(review.UserId, userId, StringComparison.Ordinal))
        {
            return new ReviewMutationResult(false, null, "Review not found or you do not own it.");
        }

        if (review.Status is not (ReviewStatus.Pending or ReviewStatus.Approved))
        {
            return new ReviewMutationResult(false, null, "This review can no longer accept photos.");
        }

        var settings = _reviewOptions.Value;
        if (files is null || files.Count == 0)
        {
            return new ReviewMutationResult(false, null, "Choose at least one photo.");
        }

        if (review.Images.Count + files.Count > settings.MaxImagesPerReview)
        {
            return new ReviewMutationResult(false, null, $"You can attach up to {settings.MaxImagesPerReview} photos per review.");
        }

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!settings.AllowedImageExtensions.Contains(extension))
            {
                return new ReviewMutationResult(false, null, $"Only {string.Join(", ", settings.AllowedImageExtensions)} photos are accepted.");
            }

            if (file.SizeBytes <= 0 || file.SizeBytes > settings.MaxImageBytes)
            {
                return new ReviewMutationResult(false, null, "Each photo must be smaller than 5 MB.");
            }
        }

        try
        {
            var now = DateTime.UtcNow;
            foreach (var file in files)
            {
                var safeName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
                var relativePath = $"reviews/{review.Id}/{safeName}";
                var stored = await _fileStorage.SaveAsync(
                    relativePath,
                    file.Content,
                    string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType,
                    cancellationToken);

                _context.ReviewImages.Add(new ReviewImage
                {
                    ReviewId = review.Id,
                    FileName = safeName,
                    OriginalFileName = Path.GetFileName(file.FileName),
                    ContentType = file.ContentType,
                    SizeBytes = stored.SizeBytes,
                    StoragePath = stored.RelativePath,
                    CreatedAtUtc = now
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Uploaded {Count} image(s) for review {ReviewId}", files.Count, review.Id);
            return new ReviewMutationResult(true, review.Id, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review image upload failed for review {ReviewId}", review.Id);
            return new ReviewMutationResult(false, null, "We could not upload the photos. Please try again.");
        }
    }

    public async Task<ReviewHelpfulResult> ToggleHelpfulAsync(
        string userId,
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var review = await _context.ProductReviews
                    .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

                if (review is null || review.Status != ReviewStatus.Approved)
                {
                    return new ReviewHelpfulResult(false, false, 0, "This review is no longer available.");
                }

                var vote = await _context.ReviewHelpfulVotes
                    .FirstOrDefaultAsync(v => v.ReviewId == reviewId && v.UserId == userId, cancellationToken);

                if (vote is not null)
                {
                    _context.ReviewHelpfulVotes.Remove(vote);
                    review.HelpfulCount = Math.Max(0, review.HelpfulCount - 1);
                }
                else
                {
                    _context.ReviewHelpfulVotes.Add(new ReviewHelpfulVote
                    {
                        ReviewId = reviewId,
                        UserId = userId,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    review.HelpfulCount += 1;
                }

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                return new ReviewHelpfulResult(true, vote is null, review.HelpfulCount, null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toggling helpful vote on review {ReviewId} failed", reviewId);
            return new ReviewHelpfulResult(false, false, 0, "We could not update your vote. Please try again.");
        }
    }

    public async Task<MyReviewsResultDto> GetMyReviewsAsync(
        string userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, MaxPageSize);

        var baseQuery = _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
            .Where(r => r.UserId == userId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var reviews = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var productIds = reviews.Where(r => r.Product != null).Select(r => r.Product!.Id).Distinct().ToList();
        var thumbnails = productIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _context.ProductImages
                .AsNoTracking()
                .Where(i => productIds.Contains(i.ProductId) && i.IsMain)
                .Select(i => new { i.ProductId, i.FileName })
                .ToDictionaryAsync(i => i.ProductId, i => i.FileName, cancellationToken);

        var items = reviews.Select(r => new MyReviewListItemDto(
            r.Id,
            r.ProductId,
            r.Product?.Name ?? "Product",
            r.Product?.Slug ?? string.Empty,
            r.Product != null && thumbnails.TryGetValue(r.Product!.Id, out var file) ? _fileStorage.ResolveUrl(file) : null,
            r.Rating,
            r.Title,
            r.Status.ToString(),
            r.HelpfulCount,
            r.IsVerifiedPurchase,
            r.CreatedAtUtc)).ToList();

        return new MyReviewsResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount);
    }

    // ---- Helpers ----

    public async Task<ReviewProductDto?> GetReviewableProductAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => new ReviewProductDto(p.Id, p.Name, p.Slug, p.AllowReviews, p.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        return product is null || !product.AllowReviews ? null : product;
    }

    private async Task<bool> HasDeliveredPurchaseAsync(
        string userId,
        Guid productId,
        CancellationToken cancellationToken) =>
        await _context.OrderItems
            .AsNoTracking()
            .AnyAsync(oi =>
                oi.ProductId == productId &&
                oi.Order != null &&
                oi.Order.UserId == userId &&
                oi.Order.OrderStatus == OrderStatus.Delivered,
                cancellationToken);

    private async Task<OrderItem?> ResolveVerifiedOrderItemAsync(
        string userId,
        Guid productId,
        Guid? requestedOrderItemId,
        CancellationToken cancellationToken)
    {
        var qualifying = _context.OrderItems
            .Where(oi =>
                oi.ProductId == productId &&
                oi.Order != null &&
                oi.Order.UserId == userId &&
                oi.Order.OrderStatus == OrderStatus.Delivered);

        if (requestedOrderItemId.HasValue)
        {
            var orderItem = await qualifying
                .FirstOrDefaultAsync(oi => oi.Id == requestedOrderItemId.Value, cancellationToken);

            if (orderItem is not null)
            {
                return orderItem;
            }
        }

        return await qualifying
            .OrderBy(oi => oi.Order!.DeliveredAtUtc ?? oi.Order.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static RatingDistributionDto[] BuildEmptyDistribution() =>
        new[]
        {
            new RatingDistributionDto(5, 0),
            new RatingDistributionDto(4, 0),
            new RatingDistributionDto(3, 0),
            new RatingDistributionDto(2, 0),
            new RatingDistributionDto(1, 0)
        };

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
