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

public class ProductServiceTests
{
    private readonly IDistributedCache _cache = new MemoryDistributedCache(new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private ProductService CreateService(AppDbContext context)
    {
        return new ProductService(
            context,
            _cache,
            NullLogger<ProductService>.Instance,
            new CacheSettings { AbsoluteExpirationMinutes = 10 });
    }

    private static async Task<Category> SeedCategoryAsync(AppDbContext context)
    {
        var category = new Category { Name = "Test Category", Slug = "test-category" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    private static CreateProductRequest ValidRequest(Guid categoryId, string? name = null)
    {
        return new CreateProductRequest(
            name ?? "New Denim Jacket",
            "A short description",
            "<p>Full description</p>",
            categoryId,
            null, null, "Jackets", "Denim", null, null, "Women", null,
            "DJ-1001", null, 89.99m, 110.00m, 45.00m, "clothing", 0.9m,
            true, false, true, false, true, null, null, null, null, null);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_GeneratesSlugAndPersists()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);

        var created = await service.CreateAsync(ValidRequest(category.Id), CancellationToken.None);

        Assert.Equal("new-denim-jacket", created.Slug);
        Assert.Equal(89.99m, created.BasePrice);
        var stored = await context.Products.SingleAsync(p => p.Id == created.Id);
        Assert.Equal("new-denim-jacket", stored.Slug);
        Assert.True(stored.PublishedAtUtc.HasValue);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);

        await service.CreateAsync(ValidRequest(category.Id), CancellationToken.None);

        var duplicate = ValidRequest(category.Id, name: "New  Denim  Jacket");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(duplicate, CancellationToken.None));
        Assert.Contains("slug", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_NegativeBasePrice_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var request = ValidRequest(category.Id) with { BasePrice = -5m };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request, CancellationToken.None));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_CompareAtBelowBase_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var request = ValidRequest(category.Id) with { BasePrice = 100m, CompareAtPrice = 90m };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request, CancellationToken.None));
        Assert.Contains("compare at price", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_NegativeCost_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var request = ValidRequest(category.Id) with { CostPrice = -1m };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request, CancellationToken.None));
        Assert.Contains("cost price", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_StripsUnsafeHtmlFromDescription()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var request = ValidRequest(category.Id) with
        {
            FullDescription = "<p>Safe text</p><script>alert('xss')</script><img src=x onerror=alert(1)>"
        };

        await service.CreateAsync(request, CancellationToken.None);

        var stored = await context.Products.SingleAsync(p => p.Slug == "new-denim-jacket");
        Assert.Contains("Safe text", stored.FullDescription);
        Assert.DoesNotContain("<script", stored.FullDescription, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", stored.FullDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);

        var request = new UpdateProductRequest(
            Guid.NewGuid(), "Updated", "desc", "", category.Id,
            null, null, "Jackets", null, null, null, null, null, "UP-1", null,
            10m, null, null, "clothing", null, true, false, false, false, true,
            null, null, null, null, null);

        var result = await service.UpdateAsync(request, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesFields()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);

        var created = await service.CreateAsync(ValidRequest(category.Id), CancellationToken.None);

        var update = new UpdateProductRequest(
            created.Id, "Updated Jacket", "New description", "<p>New body</p>", category.Id,
            null, null, "Jackets", "Leather", null, null, null, null, "DJ-1001", "UP-JKT",
            99.00m, 120.00m, null, "clothing", null, true, true, false, false, true,
            null, null, null, null, null);

        var updated = await service.UpdateAsync(update, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("updated-jacket", updated!.Slug);
        Assert.Equal(99.00m, updated.BasePrice);
        Assert.Equal("Leather", updated.Material);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingId_RemovesProduct()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var created = await service.CreateAsync(ValidRequest(category.Id), CancellationToken.None);

        var result = await service.DeleteAsync(created.Id, CancellationToken.None);

        Assert.True(result);
        Assert.False(await context.Products.AnyAsync(p => p.Id == created.Id));
    }

    [Fact]
    public async Task DuplicateAsync_UnknownSource_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DuplicateAsync(new DuplicateProductRequest(Guid.NewGuid(), "Copy", null), CancellationToken.None));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateAsync_CopiesCoreFieldsAndGeneratesNewSku()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var source = await service.CreateAsync(ValidRequest(category.Id), CancellationToken.None);

        var copy = await service.DuplicateAsync(
            new DuplicateProductRequest(source.Id, "Denim Jacket Copy", null), CancellationToken.None);

        Assert.Equal("denim-jacket-copy", copy.Slug);
        Assert.Equal("DJ-1001-COPY", copy.BaseSku);
        Assert.False(copy.IsActive);
        Assert.Equal(89.99m, copy.BasePrice);
    }

    [Fact]
    public async Task PublishAsync_SetsActiveAndPublished()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var created = await service.CreateAsync(ValidRequest(category.Id), CancellationToken.None);

        var published = await service.PublishAsync(created.Id, CancellationToken.None);

        Assert.True(published);
        var stored = await context.Products.FindAsync(created.Id);
        Assert.True(stored!.IsActive);
        Assert.NotNull(stored.PublishedAtUtc);
    }

    [Fact]
    public async Task ArchiveAsync_DeactivatesAndClearsFlags()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        var request = ValidRequest(category.Id) with { IsFeatured = true, IsNewArrival = true };
        var created = await service.CreateAsync(request, CancellationToken.None);

        var archived = await service.ArchiveAsync(created.Id, CancellationToken.None);

        Assert.True(archived);
        var stored = await context.Products.FindAsync(created.Id);
        Assert.False(stored!.IsActive);
        Assert.False(stored.IsFeatured);
        Assert.False(stored.IsNewArrival);
        Assert.False(stored.IsBestSeller);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task IsSlugUniqueAsync_ExistingSlug_ReturnsFalse()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var category = await SeedCategoryAsync(context);
        await service.CreateAsync(ValidRequest(category.Id), CancellationToken.None);

        var unique = await service.IsSlugUniqueAsync("new-denim-jacket", cancellationToken: CancellationToken.None);
        Assert.False(unique);
    }
}
