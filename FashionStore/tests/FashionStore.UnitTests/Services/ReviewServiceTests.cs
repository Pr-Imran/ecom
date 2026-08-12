using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.DTOs.Reviews;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class ReviewServiceTests
{
    private static readonly ReviewSettings Settings = new()
    {
        MinRating = 1,
        MaxRating = 5,
        MaxImagesPerReview = 6,
        MaxImageBytes = 5242880,
        AllowedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" },
        MinBodyLength = 10,
        MaxBodyLength = 4000,
        MaxTitleLength = 200,
        AutoApproveReviews = false
    };

    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<IFileStorageService> FileStorage { get; } = new();

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fashionstore-reviews-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);
            Context.Database.EnsureCreated();

            FileStorage.Setup(f => f.ResolveUrl(It.IsAny<string>()))
                .Returns((string path) => $"/storage/{path}");
            FileStorage.Setup(f => f.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, Stream _, string _, CancellationToken _) => new StoredFileResult(path, $"/storage/{path}", 12345));
        }

        public ReviewService CreateService() =>
            new(Context, FileStorage.Object, Options.Create(Settings), NullLogger<ReviewService>.Instance);

        public ReviewService CreateService(ReviewSettings settings) =>
            new(Context, FileStorage.Object, Options.Create(settings), NullLogger<ReviewService>.Instance);
    }

    private static Product SeedProduct(AppDbContext context, bool active = true, bool allowReviews = true)
    {
        var product = new Product
        {
            Name = "Cashmere Crew Neck Sweater",
            Slug = "cashmere-crew-neck-sweater",
            CategoryId = Guid.NewGuid(),
            ProductType = "Standard",
            BaseSku = "SW-1001",
            BasePrice = 128.00m,
            TaxCategory = "Standard",
            IsActive = active,
            AllowReviews = allowReviews,
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    private static Order SeedDeliveredOrder(AppDbContext context, string userId, Guid productId, string orderNumber = "ORD-2026-000001")
    {
        var order = new Order
        {
            PublicOrderNumber = orderNumber,
            UserId = userId,
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = 128m,
            ProductDiscount = 0m,
            CouponDiscount = 0m,
            ShippingCharge = 10m,
            Tax = 5m,
            GrandTotal = 143m,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard",
            OrderStatus = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Paid,
            FulfilmentStatus = FulfilmentStatus.Fulfilled,
            DeliveredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            UpdatedAtUtc = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            ProductId = productId,
            ProductVariantId = Guid.NewGuid(),
            ProductName = "Cashmere Crew Neck Sweater",
            ProductSlug = "cashmere-crew-neck-sweater",
            Sku = "SW-1001-GREY-M",
            Quantity = 1,
            UnitPrice = 128m,
            LineTotal = 128m
        });
        context.Orders.Add(order);
        context.SaveChanges();
        return order;
    }

    private static ReviewSubmissionRequest Submit(Guid productId, int rating = 5, string body = "Great quality and perfect fit.") =>
        new(productId, rating, "Love it", body, null);

    // ---- Eligibility: verified purchase ----

    [Fact]
    public async Task GetEligibility_NoDeliveredPurchase_NotEligible()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);

        var result = await fixture.CreateService().GetEligibilityAsync("user-1", product.Id);

        Assert.False(result.IsEligible);
        Assert.False(result.AlreadyReviewed);
        Assert.Contains("delivered order", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEligibility_WithDeliveredPurchase_IsEligible()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().GetEligibilityAsync("user-1", product.Id);

        Assert.True(result.IsEligible);
    }

    [Fact]
    public async Task GetEligibility_InactiveProduct_NotEligible()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context, active: false);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().GetEligibilityAsync("user-1", product.Id);

        Assert.False(result.IsEligible);
        Assert.Contains("no longer available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEligibility_InactiveProductAllowedBySettings_IsEligible()
    {
        var fixture = new Fixture();
        var settings = new ReviewSettings
        {
            AllowReviewsForInactiveProducts = true,
            MinRating = 1,
            MaxRating = 5,
            MinBodyLength = 10,
            MaxBodyLength = 4000
        };
        var product = SeedProduct(fixture.Context, active: false);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService(settings).GetEligibilityAsync("user-1", product.Id);

        Assert.True(result.IsEligible);
    }

    // ---- Duplicate review rule ----

    [Fact]
    public async Task GetEligibility_AlreadyReviewed_NotEligible()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);
        fixture.Context.ProductReviews.Add(new ProductReview
        {
            ProductId = product.Id,
            UserId = "user-1",
            Rating = 5,
            Body = "Great quality and perfect fit.",
            Status = ReviewStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow
        });
        fixture.Context.SaveChanges();

        var result = await fixture.CreateService().GetEligibilityAsync("user-1", product.Id);

        Assert.False(result.IsEligible);
        Assert.True(result.AlreadyReviewed);
    }

    [Fact]
    public async Task GetEligibility_RejectedReview_AllowsResubmission()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);
        fixture.Context.ProductReviews.Add(new ProductReview
        {
            ProductId = product.Id,
            UserId = "user-1",
            Rating = 2,
            Body = "Too small for me.",
            Status = ReviewStatus.Rejected,
            CreatedAtUtc = DateTime.UtcNow
        });
        fixture.Context.SaveChanges();

        var result = await fixture.CreateService().GetEligibilityAsync("user-1", product.Id);

        Assert.True(result.IsEligible);
    }

    // ---- Submission ----

    [Fact]
    public async Task Submit_WithoutDeliveredPurchase_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);

        var result = await fixture.CreateService().SubmitAsync("user-1", "Jane", Submit(product.Id));

        Assert.False(result.Success);
        Assert.Contains("delivered order", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_WithDeliveredPurchase_MarksVerifiedAndPending()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().SubmitAsync("user-1", "Jane", Submit(product.Id));

        Assert.True(result.Success);
        Assert.NotNull(result.ReviewId);
        Assert.Equal("Pending", result.Status);
        Assert.False(result.IsFlagged);

        var review = await fixture.Context.ProductReviews.FindAsync(result.ReviewId);
        Assert.NotNull(review);
        Assert.True(review!.IsVerifiedPurchase);
        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Equal("user-1", review.UserId);
    }

    [Fact]
    public async Task Submit_WithAutoApprove_VisibleImmediately()
    {
        var fixture = new Fixture();
        var settings = new ReviewSettings
        {
            AutoApproveReviews = true,
            MinRating = 1,
            MaxRating = 5,
            MinBodyLength = 10,
            MaxBodyLength = 4000
        };
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService(settings).SubmitAsync("user-1", "Jane", Submit(product.Id));

        Assert.True(result.Success);
        Assert.Equal("Approved", result.Status);

        var productAfter = await fixture.Context.Products.FindAsync(product.Id);
        Assert.Equal(1, productAfter!.ReviewCount);
        Assert.Equal(5m, productAfter.AverageRating);
    }

    [Fact]
    public async Task Submit_DuplicateReview_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var service = fixture.CreateService();
        var first = await service.SubmitAsync("user-1", "Jane", Submit(product.Id));
        Assert.True(first.Success);

        var second = await service.SubmitAsync("user-1", "Jane", Submit(product.Id));

        Assert.False(second.Success);
        Assert.Contains("already reviewed", second.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_RatingOutOfRange_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().SubmitAsync("user-1", "Jane", new ReviewSubmissionRequest(product.Id, 9, "Love it", "Great quality and perfect fit.", null));

        Assert.False(result.Success);
        Assert.Contains("rating", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_ShortBody_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().SubmitAsync("user-1", "Jane", new ReviewSubmissionRequest(product.Id, 5, "Love it", "OK", null));

        Assert.False(result.Success);
        Assert.Contains("10 characters", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_InactiveProduct_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context, active: false);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().SubmitAsync("user-1", "Jane", Submit(product.Id));

        Assert.False(result.Success);
        Assert.Contains("no longer available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Unsafe / spam content ----

    [Fact]
    public async Task Submit_UnsafeHtml_StoredAsPlainText()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().SubmitAsync(
            "user-1",
            "Jane",
            new ReviewSubmissionRequest(product.Id, 5, "<b>Love it</b>", "<script>alert('x')</script> Great quality and perfect fit.", null));

        Assert.True(result.Success);

        var review = await fixture.Context.ProductReviews.FindAsync(result.ReviewId);
        Assert.NotNull(review);
        Assert.DoesNotContain("<script>", review!.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>", review.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Love it", review.Title);
    }

    [Fact]
    public async Task Submit_SpamLikeContent_Flagged()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);

        var result = await fixture.CreateService().SubmitAsync(
            "user-1",
            "Jane",
            new ReviewSubmissionRequest(product.Id, 5, "Buy now", "Check out https://spam.example.com and earn money fast with crypto.", null));

        Assert.True(result.Success);
        Assert.True(result.IsFlagged);

        var review = await fixture.Context.ProductReviews.FindAsync(result.ReviewId);
        Assert.True(review!.IsFlagged);
    }

    // ---- Rating aggregation ----

    [Fact]
    public async Task Submit_ApprovedReviews_DriveProductAggregates()
    {
        var fixture = new Fixture();
        var settings = new ReviewSettings
        {
            AutoApproveReviews = true,
            MinRating = 1,
            MaxRating = 5,
            MinBodyLength = 10,
            MaxBodyLength = 4000
        };
        var product = SeedProduct(fixture.Context);
        SeedDeliveredOrder(fixture.Context, "user-1", product.Id);
        SeedDeliveredOrder(fixture.Context, "user-2", product.Id, "ORD-2026-000002");

        var service = fixture.CreateService(settings);
        var first = await service.SubmitAsync("user-1", "Jane", new ReviewSubmissionRequest(product.Id, 5, "Love it", "Amazing quality.", null));
        var second = await service.SubmitAsync("user-2", "John", new ReviewSubmissionRequest(product.Id, 3, "Okay", "Decent but runs small.", null));
        Assert.True(first.Success);
        Assert.True(second.Success);

        var summary = await service.GetRatingSummaryAsync(product.Id);

        Assert.Equal(2, summary.ReviewCount);
        Assert.Equal(4.0m, summary.AverageRating);
        Assert.Equal(1, summary.Distribution.Single(d => d.Star == 5).Count);
        Assert.Equal(1, summary.Distribution.Single(d => d.Star == 3).Count);
        Assert.Equal(0, summary.Distribution.Single(d => d.Star == 1).Count);

        var productAfter = await fixture.Context.Products.FindAsync(product.Id);
        Assert.Equal(2, productAfter!.ReviewCount);
        Assert.Equal(4.0m, productAfter.AverageRating);
        Assert.Equal(1, productAfter.RatingCount5);
        Assert.Equal(1, productAfter.RatingCount3);
    }

    [Fact]
    public async Task GetReviews_OnlyReturnsApproved()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        fixture.Context.ProductReviews.AddRange(
            new ProductReview { ProductId = product.Id, UserId = "u1", Rating = 5, Body = "Approved one.", Status = ReviewStatus.Approved, CreatedAtUtc = DateTime.UtcNow },
            new ProductReview { ProductId = product.Id, UserId = "u2", Rating = 1, Body = "Pending one.", Status = ReviewStatus.Pending, CreatedAtUtc = DateTime.UtcNow },
            new ProductReview { ProductId = product.Id, UserId = "u3", Rating = 2, Body = "Rejected one.", Status = ReviewStatus.Rejected, CreatedAtUtc = DateTime.UtcNow });
        fixture.Context.SaveChanges();
        await ProductRatingAggregator.RecomputeAsync(fixture.Context, product.Id);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CreateService().GetReviewsAsync(product.Id, new ReviewQueryRequest(), null);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(5, result.Items[0].Rating);
        Assert.Equal(1, result.Summary.ReviewCount);
        Assert.Equal(5m, result.Summary.AverageRating);
    }

    [Fact]
    public async Task GetReviews_FiltersByRatingAndPhotos()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var withPhoto = new ProductReview { ProductId = product.Id, UserId = "u1", Rating = 5, Body = "With photo.", Status = ReviewStatus.Approved, CreatedAtUtc = DateTime.UtcNow };
        var noPhoto = new ProductReview { ProductId = product.Id, UserId = "u2", Rating = 4, Body = "No photo.", Status = ReviewStatus.Approved, CreatedAtUtc = DateTime.UtcNow };
        withPhoto.Images.Add(new ReviewImage
        {
            FileName = "photo.jpg",
            StoragePath = "reviews/1/photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 100,
            CreatedAtUtc = DateTime.UtcNow
        });
        fixture.Context.ProductReviews.AddRange(withPhoto, noPhoto);
        fixture.Context.SaveChanges();

        var service = fixture.CreateService();
        var byRating = await service.GetReviewsAsync(product.Id, new ReviewQueryRequest(Rating: 4), null);
        Assert.Single(byRating.Items);
        Assert.Equal(4, byRating.Items[0].Rating);

        var byPhotos = await service.GetReviewsAsync(product.Id, new ReviewQueryRequest(HasPhotos: true), null);
        Assert.Single(byPhotos.Items);
        Assert.Single(byPhotos.Items[0].Images);
        Assert.Equal("/storage/reviews/1/photo.jpg", byPhotos.Items[0].Images[0].Url);
    }

    // ---- Helpful votes ----

    [Fact]
    public async Task ToggleHelpful_AddsAndRemovesVote()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = new ProductReview { ProductId = product.Id, UserId = "u1", Rating = 5, Body = "Great.", Status = ReviewStatus.Approved, CreatedAtUtc = DateTime.UtcNow };
        fixture.Context.ProductReviews.Add(review);
        fixture.Context.SaveChanges();

        var service = fixture.CreateService();
        var first = await service.ToggleHelpfulAsync("u2", review.Id);
        Assert.True(first.Success);
        Assert.True(first.Voted);
        Assert.Equal(1, first.HelpfulCount);

        var second = await service.ToggleHelpfulAsync("u2", review.Id);
        Assert.True(second.Success);
        Assert.False(second.Voted);
        Assert.Equal(0, second.HelpfulCount);

        var other = await service.ToggleHelpfulAsync("u3", review.Id);
        Assert.Equal(1, other.HelpfulCount);
    }

    [Fact]
    public async Task ToggleHelpful_PendingReview_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = new ProductReview { ProductId = product.Id, UserId = "u1", Rating = 5, Body = "Great.", Status = ReviewStatus.Pending, CreatedAtUtc = DateTime.UtcNow };
        fixture.Context.ProductReviews.Add(review);
        fixture.Context.SaveChanges();

        var result = await fixture.CreateService().ToggleHelpfulAsync("u2", review.Id);

        Assert.False(result.Success);
    }

    // ---- Photo upload ownership ----

    [Fact]
    public async Task UploadImages_NonOwner_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = new ProductReview { ProductId = product.Id, UserId = "owner", Rating = 5, Body = "Great.", Status = ReviewStatus.Pending, CreatedAtUtc = DateTime.UtcNow };
        fixture.Context.ProductReviews.Add(review);
        fixture.Context.SaveChanges();

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var input = new ReviewImageInput(stream, "photo.jpg", "image/jpeg", 3);

        var result = await fixture.CreateService().UploadImagesAsync("intruder", review.Id, new[] { input });

        Assert.False(result.Success);
        Assert.Equal(0, await fixture.Context.ReviewImages.CountAsync());
    }

    [Fact]
    public async Task UploadImages_Owner_SavesAndLinks()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = new ProductReview { ProductId = product.Id, UserId = "owner", Rating = 5, Body = "Great.", Status = ReviewStatus.Pending, CreatedAtUtc = DateTime.UtcNow };
        fixture.Context.ProductReviews.Add(review);
        fixture.Context.SaveChanges();

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var input = new ReviewImageInput(stream, "photo.jpg", "image/jpeg", 3);

        var result = await fixture.CreateService().UploadImagesAsync("owner", review.Id, new[] { input });

        Assert.True(result.Success);
        var images = await fixture.Context.ReviewImages.ToListAsync();
        Assert.Single(images);
        Assert.Equal(review.Id, images[0].ReviewId);
        Assert.StartsWith("reviews/", images[0].StoragePath);
    }

    [Fact]
    public async Task UploadImages_BadExtension_Refused()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        var review = new ProductReview { ProductId = product.Id, UserId = "owner", Rating = 5, Body = "Great.", Status = ReviewStatus.Pending, CreatedAtUtc = DateTime.UtcNow };
        fixture.Context.ProductReviews.Add(review);
        fixture.Context.SaveChanges();

        using var stream = new MemoryStream(new byte[] { 1 });
        var input = new ReviewImageInput(stream, "photo.exe", "application/octet-stream", 1);

        var result = await fixture.CreateService().UploadImagesAsync("owner", review.Id, new[] { input });

        Assert.False(result.Success);
        Assert.Contains(".jpg", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---- My reviews ----

    [Fact]
    public async Task GetMyReviews_OnlyCallersReviews()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);
        fixture.Context.ProductReviews.AddRange(
            new ProductReview { ProductId = product.Id, UserId = "me", Rating = 5, Body = "Mine.", Title = "Mine", Status = ReviewStatus.Approved, CreatedAtUtc = DateTime.UtcNow },
            new ProductReview { ProductId = product.Id, UserId = "them", Rating = 4, Body = "Theirs.", Title = "Theirs", Status = ReviewStatus.Approved, CreatedAtUtc = DateTime.UtcNow });
        fixture.Context.SaveChanges();

        var result = await fixture.CreateService().GetMyReviewsAsync("me", 1, 10);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("Cashmere Crew Neck Sweater", result.Items[0].ProductName);
        Assert.Equal("cashmere-crew-neck-sweater", result.Items[0].ProductSlug);
        Assert.Equal("Approved", result.Items[0].Status);
    }

    [Fact]
    public async Task GetReviewableProduct_UnknownSlug_ReturnsNull()
    {
        var fixture = new Fixture();
        SeedProduct(fixture.Context);

        var result = await fixture.CreateService().GetReviewableProductAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetReviewableProduct_DisallowReviews_ReturnsNull()
    {
        var fixture = new Fixture();
        SeedProduct(fixture.Context, allowReviews: false);

        var result = await fixture.CreateService().GetReviewableProductAsync("cashmere-crew-neck-sweater");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetReviewableProduct_KnownSlug_ReturnsIdentity()
    {
        var fixture = new Fixture();
        var product = SeedProduct(fixture.Context);

        var result = await fixture.CreateService().GetReviewableProductAsync("cashmere-crew-neck-sweater");

        Assert.NotNull(result);
        Assert.Equal(product.Id, result!.ProductId);
        Assert.Equal("Cashmere Crew Neck Sweater", result.Name);
    }
}
