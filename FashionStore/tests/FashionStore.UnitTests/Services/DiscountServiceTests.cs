using FashionStore.Application.DTOs.Products;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace FashionStore.UnitTests.Services;

public class DiscountServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"discount-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static DiscountService CreateService(AppDbContext context)
    {
        return new DiscountService(context, NullLogger<DiscountService>.Instance);
    }

    private static CartItemDto Item(Guid productId, Guid variantId, decimal unitPrice, int quantity = 1)
    {
        return new CartItemDto(
            CartItemId: Guid.NewGuid(),
            ProductId: productId,
            VariantId: variantId,
            ProductName: "Test Product",
            Slug: "test-product",
            BrandName: null,
            ImageUrl: null,
            ImageCardUrl: null,
            ImageAltText: null,
            Sku: "TP-001",
            ColourName: null,
            SizeName: null,
            UnitPrice: unitPrice,
            CompareAtPrice: null,
            DiscountPercent: null,
            Quantity: quantity,
            LineTotal: Math.Round(unitPrice * quantity, 2),
            AvailableStock: 10,
            IsAvailable: true,
            IsInStock: true,
            IsActive: true,
            UnavailableReason: null);
    }

    private static async Task<(Guid productId, Guid categoryId)> SeedProductAsync(
        AppDbContext context,
        decimal price = 100m,
        Guid? categoryId = null,
        Guid? brandId = null)
    {
        var category = new Category { Name = "Category", Slug = "category" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        var effectiveCategoryId = categoryId ?? category.Id;

        var brand = new Brand { Name = "Brand", Slug = "brand" };
        context.Brands.Add(brand);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Product",
            Slug = $"test-product-{Guid.NewGuid():N}",
            CategoryId = effectiveCategoryId,
            BrandId = brandId ?? brand.Id,
            BaseSku = "TP-001",
            BasePrice = price,
            IsActive = true,
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        return (product.Id, effectiveCategoryId);
    }

    private static async Task<Guid> SeedCouponAsync(
        AppDbContext context,
        string code = "SAVE10",
        DiscountType discountType = DiscountType.Percentage,
        decimal discountValue = 10m,
        decimal? maxDiscountAmount = null,
        decimal? minOrderValue = null,
        DateTime? startAtUtc = null,
        DateTime? endAtUtc = null,
        int? totalUsageLimit = null,
        int perCustomerLimit = 1,
        bool isActive = true,
        bool isFirstOrderOnly = false,
        bool isFreeShipping = false,
        IReadOnlyList<Guid>? productIds = null,
        IReadOnlyList<Guid>? categoryIds = null,
        IReadOnlyList<Guid>? brandIds = null,
        IReadOnlyList<Guid>? excludedProductIds = null,
        string? customerId = null)
    {
        var now = DateTime.UtcNow;
        var coupon = new Coupon
        {
            Code = code,
            NormalizedCode = code.Trim().ToUpperInvariant(),
            Name = $"Coupon {code}",
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountAmount = maxDiscountAmount,
            MinOrderValue = minOrderValue,
            IsFreeShipping = isFreeShipping,
            IsActive = isActive,
            IsAutoApply = false,
            IsFirstOrderOnly = isFirstOrderOnly,
            TotalUsageLimit = totalUsageLimit,
            PerCustomerLimit = perCustomerLimit,
            StartAtUtc = startAtUtc,
            EndAtUtc = endAtUtc,
            CustomerId = customerId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        foreach (var id in productIds ?? Array.Empty<Guid>())
        {
            context.CouponProducts.Add(new CouponProduct { CouponId = coupon.Id, ProductId = id });
        }

        foreach (var id in categoryIds ?? Array.Empty<Guid>())
        {
            context.CouponCategories.Add(new CouponCategory { CouponId = coupon.Id, CategoryId = id });
        }

        foreach (var id in brandIds ?? Array.Empty<Guid>())
        {
            context.CouponBrands.Add(new CouponBrand { CouponId = coupon.Id, BrandId = id });
        }

        foreach (var id in excludedProductIds ?? Array.Empty<Guid>())
        {
            context.CouponExcludedProducts.Add(new CouponExcludedProduct { CouponId = coupon.Id, ProductId = id });
        }

        await context.SaveChangesAsync();
        return coupon.Id;
    }

    private static async Task<Guid> SeedPromotionAsync(
        AppDbContext context,
        string name = "Promo",
        DiscountType discountType = DiscountType.Percentage,
        decimal discountValue = 10m,
        decimal? maxDiscountAmount = null,
        int minQuantity = 1,
        int priority = 0,
        bool isStackable = false,
        Guid? productId = null,
        Guid? categoryId = null,
        Guid? brandId = null,
        bool isActive = true,
        DateTime? startAtUtc = null,
        DateTime? endAtUtc = null)
    {
        var promotion = new Promotion
        {
            Name = name,
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountAmount = maxDiscountAmount,
            MinQuantity = minQuantity,
            Priority = priority,
            IsStackable = isStackable,
            IsActive = isActive,
            StartAtUtc = startAtUtc,
            EndAtUtc = endAtUtc,
            ProductId = productId,
            CategoryId = categoryId,
            BrandId = brandId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.Promotions.Add(promotion);
        await context.SaveChangesAsync();
        return promotion.Id;
    }

    private static async Task SeedUsageAsync(
        AppDbContext context,
        Guid couponId,
        string userId,
        int count,
        decimal amount = 5m,
        string? orderId = "ORD-1")
    {
        for (var i = 0; i < count; i++)
        {
            context.CouponUsages.Add(new CouponUsage
            {
                CouponId = couponId,
                UserId = userId,
                OrderId = orderId,
                AmountDiscounted = amount,
                UsedAtUtc = DateTime.UtcNow.AddDays(-1)
            });
        }

        await context.SaveChangesAsync();
    }

    // --- Percentage and fixed coupons ---

    [Fact]
    public async Task PercentageCoupon_AppliesCorrectDiscount()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", DiscountType.Percentage, 10m);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal(10m, result.CouponDiscount);
        Assert.Equal(90m, result.Total);
    }

    [Fact]
    public async Task FixedCoupon_AppliesCorrectDiscount()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE20", DiscountType.FixedAmount, 20m);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE20", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal(20m, result.CouponDiscount);
        Assert.Equal(80m, result.Total);
    }

    [Fact]
    public async Task FixedCoupon_NeverGoesBelowZero()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE200", DiscountType.FixedAmount, 200m);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE200", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal(0m, result.Total);
        Assert.Equal(100m, result.CouponDiscount);
    }

    // --- Date validity ---

    [Fact]
    public async Task Coupon_NotYetStarted_IsRejected()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", startAtUtc: DateTime.UtcNow.AddDays(2));
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("not active yet", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpiredCoupon_IsRejected()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", endAtUtc: DateTime.UtcNow.AddDays(-2));
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("expired", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InactiveCoupon_IsRejected()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", isActive: false);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("no longer active", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    // --- Minimum order and maximum discount ---

    [Fact]
    public async Task Coupon_BelowMinimumOrder_IsRejected()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", minOrderValue: 150m);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("minimum order", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Coupon_MeetingMinimumOrder_IsApplied()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", minOrderValue: 100m);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal(10m, result.CouponDiscount);
    }

    [Fact]
    public async Task Coupon_MaxDiscount_CapsAppliedAmount()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE50", DiscountType.Percentage, 50m, maxDiscountAmount: 10m);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE50", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal(10m, result.CouponDiscount);
    }

    // --- Usage limits ---

    [Fact]
    public async Task Coupon_TotalUsageLimitExceeded_IsRejected()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        var couponId = await SeedCouponAsync(context, "SAVE10", totalUsageLimit: 1);
        await SeedUsageAsync(context, couponId, UserB, 1);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("usage limit", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Coupon_PerCustomerLimitExceeded_IsRejectedForThatCustomer()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        var couponId = await SeedCouponAsync(context, "SAVE10", perCustomerLimit: 1);
        await SeedUsageAsync(context, couponId, UserA, 1);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("already used", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Coupon_PerCustomerLimit_AllowsOtherCustomers()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        var couponId = await SeedCouponAsync(context, "SAVE10", perCustomerLimit: 1);
        await SeedUsageAsync(context, couponId, UserB, 1);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.True(result.CouponApplied);
    }

    [Fact]
    public async Task FirstOrderOnlyCoupon_RejectsReturningCustomer()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        var couponId = await SeedCouponAsync(context, "SAVE10", isFirstOrderOnly: true);
        await SeedUsageAsync(context, couponId, UserA, 1, orderId: "ORD-1");
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("first order", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    // --- Restrictions ---

    [Fact]
    public async Task Coupon_RestrictedToOtherProduct_IsRejected()
    {
        using var context = CreateContext();
        var (productA, _) = await SeedProductAsync(context);
        var (productB, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", productIds: new[] { productB });
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productA, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("does not apply", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Coupon_RestrictedToProduct_AppliesToThatProduct()
    {
        using var context = CreateContext();
        var (productA, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", productIds: new[] { productA });
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productA, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal(10m, result.CouponDiscount);
    }

    [Fact]
    public async Task Coupon_RestrictedToOtherCategory_IsRejected()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        var otherCategory = new Category { Name = "Other", Slug = "other" };
        context.Categories.Add(otherCategory);
        await context.SaveChangesAsync();
        await SeedCouponAsync(context, "SAVE10", categoryIds: new[] { otherCategory.Id });
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("does not apply", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Coupon_CategoryRestriction_AppliesToMatchingCategory()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", categoryIds: new[] { categoryId });
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal(10m, result.CouponDiscount);
    }

    [Fact]
    public async Task Coupon_ExcludedProduct_IsRejected()
    {
        using var context = CreateContext();
        var (productA, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", excludedProductIds: new[] { productA });
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productA, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("does not apply", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerSpecificCoupon_RejectsOtherCustomers()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", customerId: UserB);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("selected customers", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomerSpecificCoupon_AppliesToTargetedCustomer()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10", customerId: UserA);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        Assert.True(result.CouponApplied);
    }

    // --- Case insensitivity ---

    [Fact]
    public async Task Coupon_Code_MatchesCaseInsensitively()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        await SeedCouponAsync(context, "SAVE10");
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "save10", CancellationToken.None);

        Assert.True(result.CouponApplied);
        Assert.Equal("SAVE10", result.AppliedCouponCode);
    }

    [Fact]
    public async Task UnknownCouponCode_ReturnsNotFoundMessage()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedProductAsync(context);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "NOPE", CancellationToken.None);

        Assert.False(result.CouponApplied);
        Assert.Contains("not found", result.CouponMessage, StringComparison.OrdinalIgnoreCase);
    }

    // --- Promotions ---

    [Fact]
    public async Task Promotion_AppliesPercentageDiscount()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Category Sale", categoryId: categoryId);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, null, CancellationToken.None);

        Assert.Equal(10m, result.PromotionsDiscount);
        Assert.Equal(90m, result.Total);
    }

    [Fact]
    public async Task Promotion_MinQuantity_NotMet_SkipsPromotion()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Buy 2+", categoryId: categoryId, minQuantity: 2);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m, quantity: 1) }, null, CancellationToken.None);

        Assert.Equal(0m, result.PromotionsDiscount);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public async Task Promotion_MinQuantity_Met_AppliesDiscount()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Buy 2+", categoryId: categoryId, minQuantity: 2);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m, quantity: 2) }, null, CancellationToken.None);

        Assert.Equal(20m, result.PromotionsDiscount);
        Assert.Equal(180m, result.Total);
    }

    [Fact]
    public async Task StackablePromotions_CombineInPriorityOrder()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Sale A", categoryId: categoryId, discountValue: 10m, priority: 0, isStackable: true);
        await SeedPromotionAsync(context, "Sale B", categoryId: categoryId, discountValue: 10m, priority: 1, isStackable: true);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, null, CancellationToken.None);

        // 10% of 100 = 10, then 10% of 90 = 9 → total 19
        Assert.Equal(19m, result.PromotionsDiscount);
        Assert.Equal(81m, result.Total);
    }

    [Fact]
    public async Task NonStackablePromotion_BlocksLaterPromotions()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Sale A", categoryId: categoryId, discountValue: 10m, priority: 0, isStackable: false);
        await SeedPromotionAsync(context, "Sale B", categoryId: categoryId, discountValue: 10m, priority: 1, isStackable: true);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, null, CancellationToken.None);

        Assert.Equal(10m, result.PromotionsDiscount);
        Assert.Equal(90m, result.Total);
    }

    [Fact]
    public async Task InactivePromotion_IsNotApplied()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Sale", categoryId: categoryId, isActive: false);
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, null, CancellationToken.None);

        Assert.Equal(0m, result.PromotionsDiscount);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public async Task ExpiredPromotion_IsNotApplied()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Sale", categoryId: categoryId, endAtUtc: DateTime.UtcNow.AddDays(-1));
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, null, CancellationToken.None);

        Assert.Equal(0m, result.PromotionsDiscount);
    }

    // --- Coupon + promotion interplay ---

    [Fact]
    public async Task Coupon_AppliesAfterPromotions()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Category Sale", categoryId: categoryId, discountValue: 10m);
        await SeedCouponAsync(context, "SAVE10");
        var service = CreateService(context);

        var result = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        // Promotion 10 off, then the coupon caps at the remaining payable (90);
        // the percentage is computed on the eligible pre-promotion base (10% of 100)
        // and clamped to the remaining payable, so the coupon discount is 10.
        Assert.Equal(10m, result.PromotionsDiscount);
        Assert.Equal(10m, result.CouponDiscount);
        Assert.Equal(80m, result.Total);
    }

    [Fact]
    public async Task Breakdown_IsDeterministic()
    {
        using var context = CreateContext();
        var (productId, categoryId) = await SeedProductAsync(context);
        await SeedPromotionAsync(context, "Sale B", categoryId: categoryId, discountValue: 5m, isStackable: true);
        await SeedPromotionAsync(context, "Sale A", categoryId: categoryId, discountValue: 10m, isStackable: true);
        await SeedCouponAsync(context, "SAVE10");
        var service = CreateService(context);

        var first = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);
        var second = await service.CalculateAsync(UserA, new[] { Item(productId, Guid.NewGuid(), 100m) }, "SAVE10", CancellationToken.None);

        var firstLabels = first.Breakdown.Select(b => b.Label).ToArray();
        var secondLabels = second.Breakdown.Select(b => b.Label).ToArray();
        Assert.Equal(firstLabels, secondLabels);
        Assert.True(firstLabels.Length >= 3);
    }

    // --- RecordUsageAsync concurrency ---

    [Fact]
    public async Task RecordUsage_ConcurrentRedemptions_RespectTotalLimit()
    {
        var dbName = $"discount-concurrent-{Guid.NewGuid()}";
        var sharedRoot = new InMemoryDatabaseRoot();
        AppDbContext CreateShared()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName, sharedRoot)
                .Options;
            return new AppDbContext(options);
        }

        Guid couponId;
        using (var seedContext = CreateShared())
        {
            couponId = await SeedCouponAsync(seedContext, "SAVE10", totalUsageLimit: 2, perCustomerLimit: 100);
        }

        var tasks = Enumerable.Range(0, 5)
            .Select(i =>
            {
                var context = CreateShared();
                var service = CreateService(context);
                return service.RecordUsageAsync(couponId, $"user-{i}", 10m, $"ORD-{i}", CancellationToken.None);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(2, results.Count(r => r));

        using var verifyContext = CreateShared();
        Assert.Equal(2, await verifyContext.CouponUsages.CountAsync());
    }

    [Fact]
    public async Task RecordUsage_RespectsPerCustomerLimit()
    {
        using var context = CreateContext();
        var couponId = await SeedCouponAsync(context, "SAVE10", perCustomerLimit: 1);
        var service = CreateService(context);

        Assert.True(await service.RecordUsageAsync(couponId, UserA, 10m, "ORD-1", CancellationToken.None));
        Assert.False(await service.RecordUsageAsync(couponId, UserA, 10m, "ORD-2", CancellationToken.None));
        Assert.True(await service.RecordUsageAsync(couponId, UserB, 10m, "ORD-3", CancellationToken.None));
    }
}
