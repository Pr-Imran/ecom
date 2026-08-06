using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionStore.UnitTests.Services;

public class CartServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"cart-{Guid.NewGuid()}")
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

    private static CartService CreateService(AppDbContext context)
    {
        var storage = CreateStorageStub();
        return new CartService(
            context,
            new AddToCartService(context, NullLogger<AddToCartService>.Instance),
            storage,
            NullLogger<CartService>.Instance);
    }

    private static async Task<(Guid productId, Guid variantId)> SeedAsync(
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

        return (product.Id, variantId ?? Guid.Empty);
    }

    [Fact]
    public async Task Add_WithValidVariant_AddsLine()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(1, await context.CartItems.CountAsync(c => c.UserId == UserA));
    }

    [Fact]
    public async Task Add_SameVariant_CombinesQuantities()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);
        var second = await service.AddAsync(UserA, productId, variantId, 3, CancellationToken.None);

        Assert.True(second.Success);
        Assert.Equal(5, second.ItemCount);
        var line = await context.CartItems.SingleAsync(c => c.UserId == UserA);
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public async Task Add_ExceedingStock_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, stock: 3);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, 4, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("stock", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Add_ExceedingMaxQuantity_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, stock: 500);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, 100, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Add_InactiveProduct_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, productActive: false);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, 1, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Add_InactiveVariant_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, variantActive: false);
        var service = CreateService(context);

        var result = await service.AddAsync(UserA, productId, variantId, 1, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Add_OneCustomerCannotMutateAnothersCart()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);
        var dataForB = await service.GetCartAsync(UserB, CancellationToken.None);

        Assert.Empty(dataForB.Items);
        Assert.Equal(0, await service.GetCountAsync(UserB, CancellationToken.None));
    }

    [Fact]
    public async Task GetCart_ComputesLineTotalsAndSubtotal()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);

        var data = await service.GetCartAsync(UserA, CancellationToken.None);

        Assert.Single(data.Items);
        Assert.True(data.IsAuthenticated);
        Assert.Equal(2, data.ItemCount);
        Assert.Equal(54.99m * 2, data.Subtotal);
        Assert.True(data.Items[0].IsAvailable);
        Assert.Equal("Test Product", data.Items[0].ProductName);
        Assert.Equal("TP-001-XL", data.Items[0].Sku);
    }

    [Fact]
    public async Task GetCart_FlagsOutOfStockVariant_AndExcludesFromSubtotal()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, stock: 0);
        var service = CreateService(context);

        // Can't add out-of-stock directly; seed the line manually to simulate stock drop.
        context.CartItems.Add(new CartItem
        {
            UserId = UserA,
            ProductId = productId,
            ProductVariantId = variantId,
            Quantity = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var data = await service.GetCartAsync(UserA, CancellationToken.None);

        Assert.Single(data.Items);
        Assert.False(data.Items[0].IsAvailable);
        Assert.False(data.Items[0].IsInStock);
        Assert.Equal(0m, data.Subtotal);
        Assert.True(data.HasUnavailableItems);
        Assert.Contains("stock", data.Items[0].UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCart_FlagsInactiveVariant_AndExcludesFromSubtotal()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 1, CancellationToken.None);

        var variant = await context.ProductVariants.SingleAsync(v => v.Id == variantId);
        variant.IsActive = false;
        await context.SaveChangesAsync();

        var data = await service.GetCartAsync(UserA, CancellationToken.None);

        Assert.Single(data.Items);
        Assert.False(data.Items[0].IsAvailable);
        Assert.Equal(0m, data.Subtotal);
        Assert.Contains("unavailable", data.Items[0].UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCart_FlagsInactiveProduct_AndExcludesFromSubtotal()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 1, CancellationToken.None);

        var product = await context.Products.SingleAsync();
        product.IsActive = false;
        await context.SaveChangesAsync();

        var data = await service.GetCartAsync(UserA, CancellationToken.None);

        Assert.Single(data.Items);
        Assert.False(data.Items[0].IsAvailable);
        Assert.Equal(0m, data.Subtotal);
    }

    [Fact]
    public async Task GetCart_FlagsQuantityExceedingStock()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, stock: 2);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);

        var variant = await context.ProductVariants.SingleAsync(v => v.Id == variantId);
        variant.StockQuantity = 1;
        await context.SaveChangesAsync();

        var data = await service.GetCartAsync(UserA, CancellationToken.None);

        Assert.Single(data.Items);
        Assert.False(data.Items[0].IsAvailable);
        Assert.Equal(0m, data.Subtotal);
        Assert.Contains("stock", data.Items[0].UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateQuantity_ChangesLineQuantity()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 1, CancellationToken.None);

        var result = await service.UpdateQuantityAsync(UserA, productId, variantId, 4, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(4, result.ItemCount);
        var line = await context.CartItems.SingleAsync(c => c.UserId == UserA);
        Assert.Equal(4, line.Quantity);
    }

    [Fact]
    public async Task UpdateQuantity_AboveStock_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, stock: 3);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 1, CancellationToken.None);

        var result = await service.UpdateQuantityAsync(UserA, productId, variantId, 5, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateQuantity_ForMissingLine_Fails()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.UpdateQuantityAsync(UserA, productId, variantId, 2, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("no longer in your cart", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remove_DeletesLine()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);

        var result = await service.RemoveAsync(UserA, productId, variantId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ItemCount);
        Assert.Empty(await context.CartItems.Where(c => c.UserId == UserA).ToListAsync());
    }

    [Fact]
    public async Task Remove_MissingLine_StillReturnsSuccess()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.RemoveAsync(UserA, productId, variantId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public async Task Clear_RemovesAllLines()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);

        var result = await service.ClearAsync(UserA, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.ItemCount);
        Assert.Empty(await context.CartItems.Where(c => c.UserId == UserA).ToListAsync());
    }

    [Fact]
    public async Task GetCount_ReturnsSumOfQuantities()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);
        await service.AddAsync(UserA, productId, variantId, 3, CancellationToken.None);

        Assert.Equal(5, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAnonymous_ReturnsHydratedItems()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var data = await service.ResolveAnonymousAsync(
            new[] { new AnonymousCartEntry(productId, variantId, 3) },
            CancellationToken.None);

        Assert.Single(data.Items);
        Assert.False(data.IsAuthenticated);
        Assert.Equal(3, data.ItemCount);
        Assert.Equal(54.99m * 3, data.Subtotal);
        Assert.Equal("Test Product", data.Items[0].ProductName);
    }

    [Fact]
    public async Task ResolveAnonymous_DeduplicatesEntries()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var data = await service.ResolveAnonymousAsync(
            new[]
            {
                new AnonymousCartEntry(productId, variantId, 2),
                new AnonymousCartEntry(productId, variantId, 3)
            },
            CancellationToken.None);

        Assert.Single(data.Items);
        Assert.Equal(2, data.ItemCount);
    }

    [Fact]
    public async Task ResolveAnonymous_FlagsInactiveProduct()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, productActive: false);
        var service = CreateService(context);

        var data = await service.ResolveAnonymousAsync(
            new[] { new AnonymousCartEntry(productId, variantId, 1) },
            CancellationToken.None);

        Assert.Single(data.Items);
        Assert.False(data.Items[0].IsAvailable);
        Assert.Equal(0m, data.Subtotal);
    }

    [Fact]
    public async Task ResolveAnonymous_EmptyEntries_ReturnsEmpty()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var data = await service.ResolveAnonymousAsync(Array.Empty<AnonymousCartEntry>(), CancellationToken.None);

        Assert.Empty(data.Items);
        Assert.Equal(0, data.ItemCount);
    }

    [Fact]
    public async Task Merge_CombinesAnonymousIntoPersistedCart()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);
        await service.AddAsync(UserA, productId, variantId, 2, CancellationToken.None);

        var merged = await service.MergeAsync(
            UserA,
            new[] { new AnonymousCartEntry(productId, variantId, 3) },
            CancellationToken.None);

        Assert.Equal(1, merged);
        Assert.Equal(5, await service.GetCountAsync(UserA, CancellationToken.None));
        var line = await context.CartItems.SingleAsync(c => c.UserId == UserA);
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public async Task Merge_DeduplicatesAnonymousEntries()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        var merged = await service.MergeAsync(
            UserA,
            new[]
            {
                new AnonymousCartEntry(productId, variantId, 2),
                new AnonymousCartEntry(productId, variantId, 3)
            },
            CancellationToken.None);

        Assert.Equal(1, merged);
        Assert.Equal(2, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task Merge_SkipsInactiveProduct()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, productActive: false);
        var service = CreateService(context);

        var merged = await service.MergeAsync(
            UserA,
            new[] { new AnonymousCartEntry(productId, variantId, 1) },
            CancellationToken.None);

        Assert.Equal(0, merged);
        Assert.Equal(0, await service.GetCountAsync(UserA, CancellationToken.None));
    }

    [Fact]
    public async Task Merge_CapsQuantityToAvailableStock()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context, stock: 3);
        var service = CreateService(context);

        var merged = await service.MergeAsync(
            UserA,
            new[] { new AnonymousCartEntry(productId, variantId, 10) },
            CancellationToken.None);

        Assert.Equal(1, merged);
        var line = await context.CartItems.SingleAsync(c => c.UserId == UserA);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public async Task GetCart_PurgesExpiredLines()
    {
        using var context = CreateContext();
        var (productId, variantId) = await SeedAsync(context);
        var service = CreateService(context);

        context.CartItems.Add(new CartItem
        {
            UserId = UserA,
            ProductId = productId,
            ProductVariantId = variantId,
            Quantity = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-40),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-40)
        });
        await context.SaveChangesAsync();

        var data = await service.GetCartAsync(UserA, CancellationToken.None);

        Assert.Empty(data.Items);
        Assert.Equal(0, await context.CartItems.CountAsync(c => c.UserId == UserA));
    }
}
