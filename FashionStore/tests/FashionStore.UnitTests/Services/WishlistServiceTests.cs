using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionStore.UnitTests.Services;

public class WishlistServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"wishlist-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static IFileStorageService CreateStorageStub()
    {
        var mock = new Mock<IFileStorageService>();
        mock.Setup(s => s.ResolveUrl(It.IsAny<string>()))
            .Returns<string?>(path => string.IsNullOrWhiteSpace(path) ? string.Empty : $"https://stub.local/{path}");
        return mock.Object;
    }

    private static WishlistService CreateService(
        AppDbContext context,
        IAddToCartService? addToCartService = null,
        ICatalogService? catalogService = null)
    {
        var storage = CreateStorageStub();
        return new WishlistService(
            context,
            addToCartService ?? new AddToCartService(context, NullLogger<AddToCartService>.Instance),
            catalogService ?? new CatalogService(context, storage, NullLogger<CatalogService>.Instance),
            storage,
            NullLogger<WishlistService>.Instance);
    }

    private static async Task<(Guid productId, Guid? variantId)> SeedAsync(
        AppDbContext context,
        bool productActive = true,
        bool variantActive = true,
        int stock = 10,
        bool hasVariation = true)
    {
        var category = new Category { Name = "Test Category", Slug = "test-category" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Product",
            Slug = "test-product",
            CategoryId = category.Id,
            BaseSku = "TP-001",
            BasePrice = 49.99m,
            CompareAtPrice = 69.99m,
            IsActive = productActive,
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        Guid? variantId = null;
        if (hasVariation)
        {
            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = "TP-001-XL",
                Price = 54.99m,
                IsActive = variantActive,
                IsDefault = true,
                StockQuantity = stock,
                ReservedStock = 0
            };
            context.ProductVariants.Add(variant);
            await context.SaveChangesAsync();
            variantId = variant.Id;
        }

        return (product.Id, variantId);
    }

    [Fact]
    public async Task Add_WithValidProduct_AddsEntry()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(1, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task Add_SameProductAndVariant_DoesNotDuplicate()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var first = await service.AddAsync(UserA, productId, variantId, CancellationToken.None);
        var second = await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, second.ItemCount);
        Assert.Equal(1, await context.WishlistItems.CountAsync(w => w.UserId == UserA));
    }

    [Fact]
    public async Task Add_SameProductDifferentVariant_AllowsSeparateEntries()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, hasVariation: false);
        var product = await context.Products.SingleAsync();
        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = "TP-001-SM",
            Price = 44.99m,
            IsActive = true,
            StockQuantity = 5,
            ReservedStock = 0
        };
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await service.AddAsync(UserA, productId, variantId, CancellationToken.None);
        await service.AddAsync(UserA, productId, variant.Id, CancellationToken.None);

        Assert.Equal(2, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task Add_WithInactiveProduct_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, productActive: false);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Add_WithDeletedProduct_Fails()
    {
        using var context = CreateContext();
        var (_, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, Guid.NewGuid(), variantId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Add_WithInactiveVariant_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, variantActive: false);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Add_VariantFromDifferentProduct_Fails()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedAsync(context);
        var otherCategory = new Category { Name = "Other", Slug = "other" };
        context.Categories.Add(otherCategory);
        await context.SaveChangesAsync();
        var otherProduct = new Product
        {
            Name = "Other",
            Slug = "other-product",
            CategoryId = otherCategory.Id,
            BaseSku = "OP-001",
            BasePrice = 10m,
            IsActive = true,
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(otherProduct);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, otherProduct.Id, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetWishlist_ExcludesInactiveProduct()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        var product = await context.Products.SingleAsync();
        product.IsActive = false;
        await context.SaveChangesAsync();

        var data = await service.GetWishlistAsync(UserA, null, CancellationToken.None);

        Assert.Empty(data.Items);
        Assert.Equal(0, data.ItemCount);
    }

    [Fact]
    public async Task GetWishlist_ExcludesDeletedProduct()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        var product = await context.Products.SingleAsync();
        context.Products.Remove(product);
        await context.SaveChangesAsync();

        var data = await service.GetWishlistAsync(UserA, null, CancellationToken.None);

        Assert.Empty(data.Items);
    }

    [Fact]
    public async Task GetWishlist_OneCustomerCannotSeeAnotherCustomersItems()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        await service.AddAsync(UserA, productId, variantId, CancellationToken.None);
        var dataForB = await service.GetWishlistAsync(UserB, null, CancellationToken.None);

        Assert.Empty(dataForB.Items);
        Assert.Equal(0, await service.GetCountAsync(UserB, CancellationToken.None));
    }

    [Fact]
    public async Task Remove_ByProduct_RemovesEntry()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        var result = await service.RemoveByProductAsync(UserA, productId, variantId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Remove_ByProduct_OfAnotherCustomer_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        var result = await service.RemoveByProductAsync(UserB, productId, variantId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task MoveToCart_WithValidVariant_SucceedsAndRemoves()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        var add = await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        var itemId = (await context.WishlistItems.SingleAsync()).Id;
        var result = await service.MoveToCartAsync(UserA, itemId, 1, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ItemCount);
        Assert.Empty(await context.WishlistItems.Where(w => w.UserId == UserA).ToListAsync());
    }

    [Fact]
    public async Task MoveToCart_WithOutOfStockVariant_FailsAndKeepsItem()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, stock: 0);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, CancellationToken.None);

        var itemId = (await context.WishlistItems.SingleAsync()).Id;
        var result = await service.MoveToCartAsync(UserA, itemId, 1, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(1, await context.WishlistItems.CountAsync(w => w.UserId == UserA));
    }

    [Fact]
    public async Task MoveToCart_WithoutSavedVariant_ResolvesDefaultVariant()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, null, CancellationToken.None);

        var itemId = (await context.WishlistItems.SingleAsync()).Id;
        var result = await service.MoveToCartAsync(UserA, itemId, 1, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task MoveToCart_WithoutAnyVariant_Fails()
    {
        using var context = CreateContext();
        var (productId, _) = await SeedAsync(context, hasVariation: false);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, null, CancellationToken.None);

        var itemId = (await context.WishlistItems.SingleAsync()).Id;
        var result = await service.MoveToCartAsync(UserA, itemId, 1, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("variation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Merge_AddsAnonymousEntries_AndSkipsDuplicates()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var anonymous = new[]
        {
            new WishlistMutationRequest(productId, variantId),
            new WishlistMutationRequest(productId, variantId)
        };

        var added = await service.MergeAsync(UserA, anonymous, CancellationToken.None);

        Assert.Equal(1, added);
        Assert.Equal(1, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task Merge_SkipsInactiveProducts()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, productActive: false);
        var service = CreateService(context);

        var added = await service.MergeAsync(
            UserA,
            new[] { new WishlistMutationRequest(productId, variantId) },
            CancellationToken.None);

        Assert.Equal(0, added);
    }

    [Fact]
    public async Task ResolveAnonymous_ReturnsHydratedItems()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var data = await service.ResolveAnonymousAsync(
            new[] { new WishlistMutationRequest(productId, variantId) },
            null,
            CancellationToken.None);

        Assert.Single(data.Items);
        Assert.False(data.IsAuthenticated);
        Assert.Equal("Test Product", data.Items[0].ProductName);
        Assert.Equal("TP-001-XL", data.Items[0].Sku);
    }

    [Fact]
    public async Task ResolveAnonymous_ExcludesInactiveProducts()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, productActive: false);
        var service = CreateService(context);

        var data = await service.ResolveAnonymousAsync(
            new[] { new WishlistMutationRequest(productId, variantId) },
            null,
            CancellationToken.None);

        Assert.Empty(data.Items);
    }
}
