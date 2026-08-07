using FashionStore.Application.DTOs.Promotions;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace FashionStore.UnitTests.Services;

public class CouponServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"coupon-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static CouponService CreateService(AppDbContext context)
    {
        return new CouponService(context, NullLogger<CouponService>.Instance);
    }

    private static CreateCouponRequest CreateRequest(
        string code = "SAVE10",
        DiscountType discountType = DiscountType.Percentage,
        decimal discountValue = 10m,
        IReadOnlyList<Guid>? productIds = null,
        IReadOnlyList<Guid>? categoryIds = null)
    {
        return new CreateCouponRequest(
            Code: code,
            Name: "Save 10",
            Description: null,
            DiscountType: discountType,
            DiscountValue: discountValue,
            MaxDiscountAmount: null,
            MinOrderValue: null,
            IsFreeShipping: false,
            IsAutoApply: false,
            IsFirstOrderOnly: false,
            TotalUsageLimit: null,
            PerCustomerLimit: 1,
            StartAtUtc: null,
            EndAtUtc: null,
            CustomerId: null,
            CategoryIds: categoryIds ?? Array.Empty<Guid>(),
            BrandIds: Array.Empty<Guid>(),
            ProductIds: productIds ?? Array.Empty<Guid>(),
            ExcludedProductIds: Array.Empty<Guid>());
    }

    [Fact]
    public async Task Create_NormalizesCodeToUpperCase()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var coupon = await service.CreateAsync(CreateRequest(code: "save10"), CancellationToken.None);

        Assert.Equal("SAVE10", coupon.Code);
        var stored = await context.Coupons.SingleAsync();
        Assert.Equal("SAVE10", stored.NormalizedCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await service.CreateAsync(CreateRequest(code: "SAVE10"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(CreateRequest(code: "save10"), CancellationToken.None));
    }

    [Fact]
    public async Task Create_PersistsProductRestrictions()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var productId = Guid.NewGuid();

        var coupon = await service.CreateAsync(CreateRequest(productIds: new[] { productId }), CancellationToken.None);

        var stored = await context.CouponProducts.SingleAsync();
        Assert.Equal(coupon.Id, stored.CouponId);
        Assert.Equal(productId, stored.ProductId);
    }

    [Fact]
    public async Task GetAll_FiltersActiveAndExpired()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        await service.CreateAsync(CreateRequest(code: "ACTIVE1"), CancellationToken.None);

        context.Coupons.Add(new Coupon
        {
            Code = "EXPIRED1",
            NormalizedCode = "EXPIRED1",
            Name = "Expired",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            StartAtUtc = DateTime.UtcNow.AddDays(-10),
            EndAtUtc = DateTime.UtcNow.AddDays(-2),
            PerCustomerLimit = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var active = await service.GetAllAsync("active", null, CancellationToken.None);
        var expired = await service.GetAllAsync("expired", null, CancellationToken.None);

        Assert.Single(active);
        Assert.Equal("ACTIVE1", active[0].Code);
        Assert.Single(expired);
        Assert.Equal("EXPIRED1", expired[0].Code);
    }

    [Fact]
    public async Task GetAll_UsageCountIsIncluded()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var coupon = await service.CreateAsync(CreateRequest(code: "SAVE10"), CancellationToken.None);

        context.CouponUsages.Add(new CouponUsage
        {
            CouponId = coupon.Id,
            UserId = "user-1",
            OrderId = "ORD-1",
            AmountDiscounted = 5m,
            UsedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var coupons = await service.GetAllAsync(null, null, CancellationToken.None);

        Assert.Single(coupons);
        Assert.Equal(1, coupons[0].UsageCount);
    }

    [Fact]
    public async Task Update_ReplacesRestrictions()
    {
        var dbName = $"coupon-update-{Guid.NewGuid()}";
        var sharedRoot = new InMemoryDatabaseRoot();
        AppDbContext CreateFresh()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName, sharedRoot)
                .Options;
            return new AppDbContext(options);
        }

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Guid couponId;
        using (var seedContext = CreateFresh())
        {
            var seedService = new CouponService(seedContext, NullLogger<CouponService>.Instance);
            couponId = (await seedService.CreateAsync(CreateRequest(code: "SAVE10", productIds: new[] { first }), CancellationToken.None)).Id;
        }

        using var context = CreateFresh();
        var service = CreateService(context);
        var updated = await service.UpdateAsync(couponId, new UpdateCouponRequest(
            couponId,
            "SAVE10",
            "Save 10",
            null,
            DiscountType.Percentage,
            15m,
            null,
            null,
            false,
            false,
            false,
            null,
            1,
            null,
            null,
            null,
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            new[] { second },
            Array.Empty<Guid>()), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(15m, updated.DiscountValue);
        var stored = await context.CouponProducts.ToListAsync();
        Assert.Single(stored);
        Assert.Equal(second, stored[0].ProductId);
    }

    [Fact]
    public async Task SetActive_TogglesCoupon()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var coupon = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.True(await service.SetActiveAsync(coupon.Id, false, CancellationToken.None));
        Assert.False((await context.Coupons.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Duplicate_CreatesCopyWithSuffixAndInactive()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var coupon = await service.CreateAsync(CreateRequest(code: "SAVE10"), CancellationToken.None);

        var copy = await service.DuplicateAsync(coupon.Id, CancellationToken.None);

        Assert.NotNull(copy);
        Assert.NotEqual(coupon.Id, copy.Id);
        Assert.Equal("SAVE10-COPY", copy.Code);
        Assert.False(copy.IsActive);
        Assert.Equal(2, await context.Coupons.CountAsync());
    }

    [Fact]
    public async Task GetUsage_FiltersByCouponAndIncludesEmail()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var couponA = await service.CreateAsync(CreateRequest(code: "AA"), CancellationToken.None);
        var couponB = await service.CreateAsync(CreateRequest(code: "BB"), CancellationToken.None);

        context.CouponUsages.AddRange(
            new CouponUsage { CouponId = couponA.Id, UserId = "user-1", OrderId = "O1", AmountDiscounted = 5m, UsedAtUtc = DateTime.UtcNow },
            new CouponUsage { CouponId = couponB.Id, UserId = "user-1", OrderId = "O2", AmountDiscounted = 3m, UsedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var usage = await service.GetUsageAsync(couponA.Id, null, CancellationToken.None);

        Assert.Single(usage);
        Assert.Equal("AA", usage[0].CouponCode);
    }

    [Fact]
    public async Task GetCustomerUsage_ReturnsOnlyThatCustomersRedemptions()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var coupon = await service.CreateAsync(CreateRequest(), CancellationToken.None);

        context.CouponUsages.AddRange(
            new CouponUsage { CouponId = coupon.Id, UserId = "user-1", OrderId = "O1", AmountDiscounted = 5m, UsedAtUtc = DateTime.UtcNow },
            new CouponUsage { CouponId = coupon.Id, UserId = "user-2", OrderId = "O2", AmountDiscounted = 5m, UsedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var usage = await service.GetCustomerUsageAsync("user-1", CancellationToken.None);

        Assert.Single(usage);
        Assert.Equal("user-1", usage[0].UserId);
    }

    [Fact]
    public async Task Create_RejectsInvalidDiscountValue()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(CreateRequest(discountValue: 0m), CancellationToken.None));
    }

    [Fact]
    public async Task Create_RejectsEndBeforeStart()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var request = new CreateCouponRequest(
            "SAVE10", "Save 10", null, DiscountType.Percentage, 10m, null, null, false, false, false, null, 1,
            DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(1), null,
            Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(request, CancellationToken.None));
    }
}
