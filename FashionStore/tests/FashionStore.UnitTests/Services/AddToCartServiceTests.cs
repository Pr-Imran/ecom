using FashionStore.Application.DTOs.Products;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FashionStore.UnitTests.Services;

public class AddToCartServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static AddToCartService CreateService(AppDbContext context)
    {
        return new AddToCartService(context, NullLogger<AddToCartService>.Instance);
    }

    private static async Task<(Product product, ProductVariant variant, ProductAttributeValue red, ProductAttributeValue small)> SeedAsync(AppDbContext context)
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
            IsActive = true,
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var colour = new ProductAttribute { Name = "Colour", Slug = "colour", IsVariationAttribute = true, DisplayType = "Swatch" };
        var size = new ProductAttribute { Name = "Size", Slug = "size", IsVariationAttribute = true, DisplayType = "Dropdown" };
        context.ProductAttributes.AddRange(colour, size);
        await context.SaveChangesAsync();

        var red = new ProductAttributeValue { ProductAttributeId = colour.Id, Name = "Red", Slug = "red", DisplayValue = "Red" };
        var small = new ProductAttributeValue { ProductAttributeId = size.Id, Name = "Small", Slug = "small", DisplayValue = "S" };
        context.ProductAttributeValues.AddRange(red, small);
        await context.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = "TP-001-RED-S",
            Price = 54.99m,
            CompareAtPrice = 69.99m,
            IsActive = true,
            IsDefault = true,
            StockQuantity = 10,
            ReservedStock = 2,
            VariantAttributeValues = new List<ProductVariantAttributeValue>
            {
                new() { AttributeValue = red },
                new() { AttributeValue = small }
            }
        };
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();

        return (product, variant, red, small);
    }

    [Fact]
    public async Task ValidateAsync_WithValidRequest_ReturnsServerComputedItem()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, variant.Id, 2),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Item);
        Assert.Equal(variant.Id, result.Item!.VariantId);
        Assert.Equal("TP-001-RED-S", result.Item.VariantSku);
        Assert.Equal(54.99m, result.Item.UnitPrice);
        Assert.Equal(69.99m, result.Item.CompareAtPrice);
        Assert.Equal("Red", result.Item.ColourName);
        Assert.Equal("Small", result.Item.SizeName);
        Assert.Equal(2, result.Item.Quantity);
        Assert.Equal(109.98m, result.Item.LineTotal);
        Assert.Equal(8, result.Item.AvailableStock);
    }

    [Fact]
    public async Task ValidateAsync_AvailableStock_SubtractsReservedStock()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, variant.Id, 9),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("stock", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ValidateAsync_QuantityBelowOne_IsRejected(int quantity)
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, variant.Id, quantity),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Quantity", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_QuantityAboveMaximum_IsRejected()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, variant.Id, 100),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("99", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_UnknownVariant_IsRejected()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, _, _, _) = await SeedAsync(context);

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, Guid.NewGuid(), 1),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("variation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_VariantFromDifferentProduct_IsRejected()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        var otherProduct = new Product
        {
            Name = "Other Product",
            Slug = "other-product",
            BaseSku = "OP-001",
            BasePrice = 10m,
            IsActive = true,
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        context.Products.Add(otherProduct);
        await context.SaveChangesAsync();

        var result = await service.ValidateAsync(
            new AddToCartRequest(otherProduct.Id, variant.Id, 1),
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_UnpublishedProduct_IsRejected()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        product.PublishedAtUtc = DateTime.UtcNow.AddDays(1);
        product.IsActive = true;
        await context.SaveChangesAsync();

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, variant.Id, 1),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("no longer available", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_InactiveVariant_IsRejected()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        variant.IsActive = false;
        await context.SaveChangesAsync();

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, variant.Id, 1),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_OutOfStockVariant_IsRejected()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (product, variant, _, _) = await SeedAsync(context);

        variant.StockQuantity = 1;
        variant.ReservedStock = 1;
        await context.SaveChangesAsync();

        var result = await service.ValidateAsync(
            new AddToCartRequest(product.Id, variant.Id, 1),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("out of stock", result.ErrorMessage);
    }
}
