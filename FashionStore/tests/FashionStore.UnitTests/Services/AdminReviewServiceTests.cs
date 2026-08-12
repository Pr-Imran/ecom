using FashionStore.Application.DTOs.Reviews;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class AdminReviewServiceTests
{
    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<IFileStorageService> FileStorage { get; } = new();

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fashionstore-admin-reviews-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);
            Context.Database.EnsureCreated();

            FileStorage.Setup(f => f.ResolveUrl(It.IsAny<string>()))
                .Returns((string path) => $"/storage/{path}");
        }

        public AdminReviewService CreateService() =>
            new(Context, FileStorage.Object, NullLogger<AdminReviewService>.Instance);
    }

    private static Product SeedProduct(AppDbContext context, string name = "Cashmere Sweater")
    {
        var product = new Product
        {
            Name = name,
            Slug = "cashmere-sweater",
            CategoryId = Guid.NewGuid(),
            ProductType = "Standard",
            BaseSku = "SW-1001",
            BasePrice = 100m,
            TaxCategory = "Standard",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static ProductReview SeedReview(AppDbContext context, Guid productId, ReviewStatus status = ReviewStatus.Pending, int rating = 5, bool flagged = false)
    {
        var review = new ProductReview
        {
            ProductId = productId,
            UserId = "user-1",
            DisplayName = "Jane Doe",
            Rating = rating,
            Title = "Great",
            Body = "Lovely quality.",
            Status = status,
            IsFlagged = flagged,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.ProductReviews.Add(review);
        context.SaveChanges();
        return review;
    }

    [Fact]
    public async Task Moderate_Approve_UpdatesStatusAndNotesAndRating()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id);

        var result = await fixture.CreateService().ModerateAsync(review.Id, new ModerateReviewRequest(ReviewStatus.Approved, "Looks genuine"), "admin-1");

        Assert.True(result.Success);
        Assert.Equal("Approved", result.Status);

        var updated = await fixture.Context.ProductReviews.FindAsync(review.Id);
        Assert.Equal(ReviewStatus.Approved, updated!.Status);
        Assert.Contains("Looks genuine", updated.ModerationNotes);
        Assert.Contains("admin-1", updated.ModerationNotes);

        var productAfter = await fixture.Context.Products.FindAsync(product.Id);
        Assert.Equal(1, productAfter!.ReviewCount);
        Assert.Equal(5m, productAfter.AverageRating);
        Assert.Equal(1, productAfter.RatingCount5);
    }

    [Fact]
    public async Task Moderate_Reject_HidesFromPublicSummary()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id, ReviewStatus.Approved);

        var result = await fixture.CreateService().ModerateAsync(review.Id, new ModerateReviewRequest(ReviewStatus.Rejected, "Spam"), "admin-1");

        Assert.True(result.Success);
        var productAfter = await fixture.Context.Products.FindAsync(product.Id);
        Assert.Equal(0, productAfter!.ReviewCount);
        Assert.Null(productAfter.AverageRating);
    }

    [Fact]
    public async Task Moderate_InvalidStatus_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id);

        var result = await fixture.CreateService().ModerateAsync(review.Id, new ModerateReviewRequest(ReviewStatus.Pending, null), "admin-1");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Moderate_UnknownReview_Refused()
    {
        var fixture = new Fixture();
        SeedProduct(fixture.Context);

        var result = await fixture.CreateService().ModerateAsync(Guid.NewGuid(), new ModerateReviewRequest(ReviewStatus.Approved, null), "admin-1");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Moderate_Hide_SuppressesWithoutDeleting()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id, ReviewStatus.Approved);

        var result = await fixture.CreateService().ModerateAsync(review.Id, new ModerateReviewRequest(ReviewStatus.Hidden, "Customer dispute"), "admin-1");

        Assert.True(result.Success);
        Assert.NotNull(await fixture.Context.ProductReviews.FindAsync(review.Id));
        var productAfter = await fixture.Context.Products.FindAsync(product.Id);
        Assert.Equal(0, productAfter!.ReviewCount);
    }

    // ---- Delete policy ----

    [Fact]
    public async Task Delete_ApprovedNotFlagged_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id, ReviewStatus.Approved);

        var result = await fixture.CreateService().DeleteAsync(review.Id, "admin-1");

        Assert.False(result.Success);
        Assert.Contains("hidden", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await fixture.Context.ProductReviews.FindAsync(review.Id));
    }

    [Fact]
    public async Task Delete_FlaggedApproved_AllowedAndRecomputes()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var keep = SeedReview(fixture.Context, product.Id, ReviewStatus.Approved, rating: 5);
        var flagged = SeedReview(fixture.Context, product.Id, ReviewStatus.Approved, rating: 1, flagged: true);

        var result = await fixture.CreateService().DeleteAsync(flagged.Id, "admin-1");

        Assert.True(result.Success);
        Assert.Null(await fixture.Context.ProductReviews.FindAsync(flagged.Id));
        Assert.NotNull(await fixture.Context.ProductReviews.FindAsync(keep.Id));

        var productAfter = await fixture.Context.Products.FindAsync(product.Id);
        Assert.Equal(1, productAfter!.ReviewCount);
        Assert.Equal(5m, productAfter.AverageRating);
    }

    [Fact]
    public async Task Delete_Pending_Allowed()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id);

        var result = await fixture.CreateService().DeleteAsync(review.Id, "admin-1");

        Assert.True(result.Success);
        Assert.Null(await fixture.Context.ProductReviews.FindAsync(review.Id));
    }

    [Fact]
    public async Task Delete_RemovesStoredImages()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id);
        fixture.Context.ReviewImages.Add(new ReviewImage
        {
            ReviewId = review.Id,
            FileName = "photo.jpg",
            StoragePath = "reviews/1/photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 10,
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CreateService().DeleteAsync(review.Id, "admin-1");

        Assert.True(result.Success);
        fixture.FileStorage.Verify(f => f.DeleteAsync("reviews/1/photo.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Notes ----

    [Fact]
    public async Task AddNote_AppendsWithoutChangingStatus()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id);

        var result = await fixture.CreateService().AddNoteAsync(review.Id, "Check order history", "admin-1");

        Assert.True(result.Success);
        var updated = await fixture.Context.ProductReviews.FindAsync(review.Id);
        Assert.Contains("Check order history", updated!.ModerationNotes);
        Assert.Equal(ReviewStatus.Pending, updated.Status);
    }

    [Fact]
    public async Task AddNote_EmptyNote_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id);

        var result = await fixture.CreateService().AddNoteAsync(review.Id, "   ", "admin-1");

        Assert.False(result.Success);
    }

    // ---- Admin list / detail ----

    [Fact]
    public async Task GetReviews_FiltersByStatusRatingVerifiedAndSearch()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context, "Cashmere Sweater");
        SeedReview(fixture.Context, product.Id, ReviewStatus.Pending, rating: 1);
        SeedReview(fixture.Context, product.Id, ReviewStatus.Approved, rating: 5);
        var verified = SeedReview(fixture.Context, product.Id, ReviewStatus.Approved, rating: 5);
        verified.IsVerifiedPurchase = true;
        verified.OrderId = Guid.NewGuid();
        await fixture.Context.SaveChangesAsync();

        var service = fixture.CreateService();

        var all = await service.GetReviewsAsync(new AdminReviewQueryRequest());
        Assert.Equal(3, all.TotalCount);

        var pending = await service.GetReviewsAsync(new AdminReviewQueryRequest(Status: ReviewStatus.Pending));
        Assert.Equal(1, pending.TotalCount);

        var fiveStar = await service.GetReviewsAsync(new AdminReviewQueryRequest(Rating: 5));
        Assert.Equal(2, fiveStar.TotalCount);

        var verifiedOnly = await service.GetReviewsAsync(new AdminReviewQueryRequest(VerifiedOnly: true));
        Assert.Equal(1, verifiedOnly.TotalCount);

        var search = await service.GetReviewsAsync(new AdminReviewQueryRequest(Search: "Cashmere"));
        Assert.Equal(3, search.TotalCount);

        var noMatch = await service.GetReviewsAsync(new AdminReviewQueryRequest(Search: "zzzz"));
        Assert.Equal(0, noMatch.TotalCount);
    }

    [Fact]
    public async Task GetReviewDetail_UnknownReview_ReturnsNull()
    {
        var fixture = new Fixture();
        SeedProduct(fixture.Context);

        var result = await fixture.CreateService().GetReviewDetailAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetReviewDetail_IncludesProductCustomerAndImages()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = SeedReview(fixture.Context, product.Id);
        fixture.Context.ReviewImages.Add(new ReviewImage
        {
            ReviewId = review.Id,
            FileName = "photo.jpg",
            StoragePath = "reviews/1/photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 10,
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CreateService().GetReviewDetailAsync(review.Id);

        Assert.NotNull(result);
        Assert.Equal("Cashmere Sweater", result!.ProductName);
        Assert.Equal("Jane Doe", result.DisplayName);
        Assert.Equal("user-1", result.UserId);
        Assert.Single(result.Images);
        Assert.Equal("/storage/reviews/1/photo.jpg", result.Images[0].Url);
    }
}
