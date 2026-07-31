using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Products;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionStore.UnitTests.Services;

public class ProductVariationServiceTests
{
    private readonly IDistributedCache _cache = new MemoryDistributedCache(new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private ProductVariationService CreateService(AppDbContext context)
    {
        return new ProductVariationService(
            context,
            _cache,
            NullLogger<ProductVariationService>.Instance,
            new CacheSettings { AbsoluteExpirationMinutes = 10 });
    }

    private static async Task<Product> SeedProductAsync(AppDbContext context)
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
            BasePrice = 49.99m
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private static async Task<(ProductAttribute colour, ProductAttribute size, ProductAttributeValue red, ProductAttributeValue blue, ProductAttributeValue small, ProductAttributeValue large)> SeedAttributesAsync(AppDbContext context)
    {
        var colour = new ProductAttribute { Name = "Colour", Slug = "colour", IsVariationAttribute = true, DisplayType = "Swatch" };
        var size = new ProductAttribute { Name = "Size", Slug = "size", IsVariationAttribute = true, DisplayType = "Dropdown" };
        context.ProductAttributes.AddRange(colour, size);
        await context.SaveChangesAsync();

        var red = new ProductAttributeValue { ProductAttributeId = colour.Id, Name = "Red", Slug = "red", DisplayValue = "Red" };
        var blue = new ProductAttributeValue { ProductAttributeId = colour.Id, Name = "Blue", Slug = "blue", DisplayValue = "Blue" };
        var small = new ProductAttributeValue { ProductAttributeId = size.Id, Name = "Small", Slug = "small", DisplayValue = "S" };
        var large = new ProductAttributeValue { ProductAttributeId = size.Id, Name = "Large", Slug = "large", DisplayValue = "L" };
        context.ProductAttributeValues.AddRange(red, blue, small, large);
        await context.SaveChangesAsync();

        return (colour, size, red, blue, small, large);
    }

    [Fact]
    public async Task GenerateCombinationsAsync_WithTwoValueDimensions_ProducesCartesianProduct()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, blue, small, large) = await SeedAttributesAsync(context);

        var request = new GenerateVariantsRequest(
            product.Id,
            new List<Guid> { red.Id, blue.Id, small.Id, large.Id },
            "TP-{colour}-{size}",
            49.99m,
            true);

        var combinations = await service.GenerateCombinationsAsync(request, CancellationToken.None);

        Assert.Equal(4, combinations.Count);
        Assert.Contains(combinations, c => c.DisplayValues["Colour"] == "Red" && c.DisplayValues["Size"] == "Small");
        Assert.Contains(combinations, c => c.DisplayValues["Colour"] == "Red" && c.DisplayValues["Size"] == "Large");
        Assert.Contains(combinations, c => c.DisplayValues["Colour"] == "Blue" && c.DisplayValues["Size"] == "Small");
        Assert.Contains(combinations, c => c.DisplayValues["Colour"] == "Blue" && c.DisplayValues["Size"] == "Large");
    }

    [Fact]
    public async Task CreateVariantAsync_WithSameCombination_IsBlocked()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, _, small, _) = await SeedAttributesAsync(context);

        var first = new CreateProductVariantRequest(
            product.Id, "TP-RED-S", null, 49.99m, null, null, null, true, true, 10, null, null,
            new List<Guid> { red.Id, small.Id });
        var second = new CreateProductVariantRequest(
            product.Id, "TP-RED-S-2", null, 49.99m, null, null, null, false, true, 5, null, null,
            new List<Guid> { red.Id, small.Id });

        await service.CreateVariantAsync(first, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateVariantAsync(second, CancellationToken.None));
        Assert.Contains("combination", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateVariantAsync_WithDuplicateSku_IsBlocked()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, blue, small, _) = await SeedAttributesAsync(context);

        var first = new CreateProductVariantRequest(
            product.Id, "TP-DUP-SKU", null, 49.99m, null, null, null, true, true, 10, null, null,
            new List<Guid> { red.Id, small.Id });
        var second = new CreateProductVariantRequest(
            product.Id, "TP-DUP-SKU", null, 59.99m, null, null, null, false, true, 5, null, null,
            new List<Guid> { blue.Id, small.Id });

        await service.CreateVariantAsync(first, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateVariantAsync(second, CancellationToken.None));
        Assert.Contains("SKU", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateVariantAsync_WhenSecondVariantMarkedDefault_UnsetsFirstDefault()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, blue, small, _) = await SeedAttributesAsync(context);

        var first = new CreateProductVariantRequest(
            product.Id, "TP-DEF-1", null, 49.99m, null, null, null, true, true, 10, null, null,
            new List<Guid> { red.Id, small.Id });
        var second = new CreateProductVariantRequest(
            product.Id, "TP-DEF-2", null, 59.99m, null, null, null, true, true, 5, null, null,
            new List<Guid> { blue.Id, small.Id });

        await service.CreateVariantAsync(first, CancellationToken.None);
        await service.CreateVariantAsync(second, CancellationToken.None);

        var variants = await service.GetVariantsByProductAsync(product.Id, CancellationToken.None);
        Assert.Single(variants, v => v.IsDefault);
        Assert.Equal("TP-DEF-2", variants.Single(v => v.IsDefault).Sku);
    }

    [Fact]
    public async Task CreateVariantAsync_WithInvalidPriceRelationship_IsBlocked()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, _, small, _) = await SeedAttributesAsync(context);

        var request = new CreateProductVariantRequest(
            product.Id, "TP-PRICE", null, 50.00m, 40.00m, null, null, true, true, 10, null, null,
            new List<Guid> { red.Id, small.Id });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateVariantAsync(request, CancellationToken.None));
        Assert.Contains("Compare at price", ex.Message);
    }

    [Fact]
    public async Task DeleteAttributeValueAsync_WhenUsedByVariant_IsBlocked()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, _, small, _) = await SeedAttributesAsync(context);

        var variant = new CreateProductVariantRequest(
            product.Id, "TP-DELETE", null, 49.99m, null, null, null, true, true, 10, null, null,
            new List<Guid> { red.Id, small.Id });
        await service.CreateVariantAsync(variant, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAttributeValueAsync(red.Id, CancellationToken.None));
        Assert.Contains("variants", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVariantByAttributeValuesAsync_ReturnsMatchingVariant()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, blue, small, large) = await SeedAttributesAsync(context);

        var redSmall = new CreateProductVariantRequest(
            product.Id, "TP-RS", null, 49.99m, null, null, null, true, true, 10, null, null,
            new List<Guid> { red.Id, small.Id });
        var blueLarge = new CreateProductVariantRequest(
            product.Id, "TP-BL", null, 59.99m, null, null, null, false, true, 5, null, null,
            new List<Guid> { blue.Id, large.Id });

        await service.CreateVariantAsync(redSmall, CancellationToken.None);
        await service.CreateVariantAsync(blueLarge, CancellationToken.None);

        var result = await service.GetVariantByAttributeValuesAsync(
            product.Id, new List<Guid> { blue.Id, large.Id }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("TP-BL", result!.Sku);
    }

    [Fact]
    public async Task HasDuplicateCombinationsAsync_DetectsExistingCombination()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var product = await SeedProductAsync(context);
        var (_, _, red, _, small, _) = await SeedAttributesAsync(context);

        var variant = new CreateProductVariantRequest(
            product.Id, "TP-HASDUP", null, 49.99m, null, null, null, true, true, 10, null, null,
            new List<Guid> { red.Id, small.Id });
        await service.CreateVariantAsync(variant, CancellationToken.None);

        var hasDuplicate = await service.HasDuplicateCombinationsAsync(
            product.Id, new List<Guid> { red.Id, small.Id }, cancellationToken: CancellationToken.None);

        Assert.True(hasDuplicate);
    }
}
