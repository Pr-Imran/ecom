using FashionStore.Application.DTOs.Reviews;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Administrative review moderation. Decisions (approve / reject / hide / delete /
/// notes) are guarded by the <c>Reviews.Manage</c> permission, each records an
/// optional moderation note, and every change recomputes the product rating aggregate
/// in the same transaction. Deletion is restricted to pending, rejected, hidden or
/// flagged reviews — clean approved reviews must be hidden instead.
/// </summary>
public sealed class AdminReviewService : IAdminReviewService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<AdminReviewService> _logger;

    public AdminReviewService(
        AppDbContext context,
        IFileStorageService fileStorage,
        ILogger<AdminReviewService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<AdminReviewListResultDto> GetReviewsAsync(
        AdminReviewQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, MaxPageSize);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var baseQuery = _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
            .AsQueryable();

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);
        }

        if (query.Rating.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Rating == query.Rating.Value);
        }

        if (query.VerifiedOnly.HasValue && query.VerifiedOnly.Value)
        {
            baseQuery = baseQuery.Where(r => r.IsVerifiedPurchase);
        }

        if (query.FlaggedOnly.HasValue && query.FlaggedOnly.Value)
        {
            baseQuery = baseQuery.Where(r => r.IsFlagged);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{search.Replace("%", "[%]").Replace("_", "[_]")}%";
            baseQuery = baseQuery.Where(r =>
                (r.Title != null && EF.Functions.Like(r.Title, pattern)) ||
                (r.Body != null && EF.Functions.Like(r.Body, pattern)) ||
                (r.DisplayName != null && EF.Functions.Like(r.DisplayName, pattern)) ||
                (r.Product != null && EF.Functions.Like(r.Product.Name, pattern)));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var reviews = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = reviews.Select(r => new AdminReviewListItemDto(
            r.Id,
            r.ProductId,
            r.Product?.Name ?? "Product",
            r.Product?.Slug ?? string.Empty,
            r.Rating,
            r.Title,
            r.Status.ToString(),
            r.IsVerifiedPurchase,
            r.IsFlagged,
            r.HelpfulCount,
            r.DisplayName ?? "Customer",
            r.CreatedAtUtc)).ToList();

        return new AdminReviewListResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount);
    }

    public async Task<AdminReviewDetailDto?> GetReviewDetailAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        var review = await _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
        {
            return null;
        }

        var productImage = review.Product is null
            ? null
            : await _context.ProductImages
                .AsNoTracking()
                .Where(i => i.ProductId == review.Product.Id && i.IsMain)
                .Select(i => i.FileName)
                .FirstOrDefaultAsync(cancellationToken);

        productImage = productImage is null ? null : _fileStorage.ResolveUrl(productImage);

        string? orderNumber = null;
        string? orderItemName = null;

        if (review.OrderId.HasValue)
        {
            orderNumber = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Id == review.OrderId.Value)
                .Select(o => o.PublicOrderNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (review.OrderItemId.HasValue)
        {
            orderItemName = await _context.OrderItems
                .AsNoTracking()
                .Where(oi => oi.Id == review.OrderItemId.Value)
                .Select(oi => oi.ProductName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new AdminReviewDetailDto(
            review.Id,
            review.ProductId,
            review.Product?.Name ?? "Product",
            review.Product?.Slug ?? string.Empty,
            productImage,
            review.Rating,
            review.Title,
            review.Body,
            review.DisplayName ?? "Customer",
            review.UserId,
            review.IsVerifiedPurchase,
            review.IsFlagged,
            review.HelpfulCount,
            review.Status.ToString(),
            review.ModerationNotes,
            review.CreatedAtUtc,
            review.UpdatedAtUtc,
            orderNumber,
            orderItemName,
            review.Images
                .OrderBy(i => i.CreatedAtUtc)
                .Select(i => new ReviewImageDto(i.Id, _fileStorage.ResolveUrl(i.StoragePath), i.ContentType))
                .ToList());
    }

    public async Task<AdminReviewMutationResult> ModerateAsync(
        Guid reviewId,
        ModerateReviewRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (request.Status is not (
            ReviewStatus.Approved or
            ReviewStatus.Rejected or
            ReviewStatus.Hidden))
        {
            return new AdminReviewMutationResult(false, reviewId, null, "Choose to approve, reject or hide the review.");
        }

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

                if (review is null)
                {
                    return new AdminReviewMutationResult(false, reviewId, null, "Review not found.");
                }

                var now = DateTime.UtcNow;
                var previous = review.Status;
                review.Status = request.Status;
                review.UpdatedAtUtc = now;
                review.UpdatedBy = actorId;
                review.ModerationNotes = AppendNote(review.ModerationNotes, $"Status {previous} → {request.Status}.", request.Notes, actorId, now);

                await _context.SaveChangesAsync(cancellationToken);
                await ProductRatingAggregator.RecomputeAsync(_context, review.ProductId, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Review {ReviewId} moderated {Previous} → {Status} by {Actor}",
                    reviewId,
                    previous,
                    request.Status,
                    actorId);

                return new AdminReviewMutationResult(true, reviewId, request.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Moderating review {ReviewId} failed", reviewId);
            return new AdminReviewMutationResult(false, reviewId, null, "We could not update the review. Please try again.");
        }
    }

    public async Task<AdminReviewMutationResult> DeleteAsync(
        Guid reviewId,
        string actorId,
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
                    .Include(r => r.Images)
                    .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

                if (review is null)
                {
                    return new AdminReviewMutationResult(false, reviewId, null, "Review not found.");
                }

                if (review.Status == ReviewStatus.Approved && !review.IsFlagged)
                {
                    return new AdminReviewMutationResult(false, reviewId, null, "Approved reviews must be hidden, not deleted. Delete is only allowed for pending, rejected, hidden or flagged reviews.");
                }

                var productId = review.ProductId;

                foreach (var image in review.Images)
                {
                    try
                    {
                        await _fileStorage.DeleteAsync(image.StoragePath, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete review image {StoragePath} during review deletion", image.StoragePath);
                    }
                }

                _context.ProductReviews.Remove(review);
                await _context.SaveChangesAsync(cancellationToken);

                await ProductRatingAggregator.RecomputeAsync(_context, productId, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation("Review {ReviewId} deleted by {Actor}", reviewId, actorId);

                return new AdminReviewMutationResult(true, reviewId, null, null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting review {ReviewId} failed", reviewId);
            return new AdminReviewMutationResult(false, reviewId, null, "We could not delete the review. Please try again.");
        }
    }

    public async Task<AdminReviewMutationResult> AddNoteAsync(
        Guid reviewId,
        string note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return new AdminReviewMutationResult(false, reviewId, null, "Enter a note.");
        }

        try
        {
            var review = await _context.ProductReviews
                .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

            if (review is null)
            {
                return new AdminReviewMutationResult(false, reviewId, null, "Review not found.");
            }

            var now = DateTime.UtcNow;
            review.ModerationNotes = AppendNote(review.ModerationNotes, null, note, actorId, now);
            review.UpdatedAtUtc = now;
            review.UpdatedBy = actorId;

            await _context.SaveChangesAsync(cancellationToken);

            return new AdminReviewMutationResult(true, reviewId, review.Status.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adding moderation note to review {ReviewId} failed", reviewId);
            return new AdminReviewMutationResult(false, reviewId, null, "We could not save the note. Please try again.");
        }
    }

    // ---- Helpers ----

    private static string? AppendNote(
        string? existing,
        string? statusLine,
        string? note,
        string actorId,
        DateTime now)
    {
        var entries = new List<string>();

        if (!string.IsNullOrWhiteSpace(statusLine))
        {
            entries.Add(statusLine.Trim());
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            entries.Add(note.Trim());
        }

        if (entries.Count == 0)
        {
            return existing;
        }

        var entry = $"[{now:yyyy-MM-dd HH:mm}] {string.Join(" ", entries)} — {actorId}";

        if (string.IsNullOrWhiteSpace(existing))
        {
            return entry;
        }

        var combined = $"{existing}\n{entry}";
        return combined.Length > 2000 ? combined[^2000..] : combined;
    }
}
