using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Recomputes a product's denormalized rating summary (average, count and per-star
/// distribution) from its approved reviews. Called inside the same transaction as
/// every moderation change so the storefront summary can never drift from the
/// approved-review set.
/// </summary>
internal static class ProductRatingAggregator
{
    public static async Task RecomputeAsync(
        AppDbContext context,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await context.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return;
        }

        var buckets = await context.ProductReviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var count1 = 0;
        var count2 = 0;
        var count3 = 0;
        var count4 = 0;
        var count5 = 0;
        var total = 0;
        var weighted = 0L;

        foreach (var bucket in buckets)
        {
            switch (bucket.Rating)
            {
                case 1: count1 = bucket.Count; break;
                case 2: count2 = bucket.Count; break;
                case 3: count3 = bucket.Count; break;
                case 4: count4 = bucket.Count; break;
                case 5: count5 = bucket.Count; break;
            }

            total += bucket.Count;
            weighted += (long)bucket.Rating * bucket.Count;
        }

        product.RatingCount1 = count1;
        product.RatingCount2 = count2;
        product.RatingCount3 = count3;
        product.RatingCount4 = count4;
        product.RatingCount5 = count5;
        product.ReviewCount = total;
        product.AverageRating = total > 0 ? Math.Round(weighted / (decimal)total, 2) : null;
        product.UpdatedAtUtc = DateTime.UtcNow;
    }
}
