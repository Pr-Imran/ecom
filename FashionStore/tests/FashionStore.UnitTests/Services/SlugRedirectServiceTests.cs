using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Seo;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class SlugRedirectServiceTests
{
    private readonly IDistributedCache _cache = new MemoryDistributedCache(
        new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-redirect-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private SlugRedirectService CreateService(AppDbContext context)
        => new(context, _cache, new CacheSettings { AbsoluteExpirationMinutes = 10 }, NullLogger<SlugRedirectService>.Instance);

    // ---- Resolve ----

    [Fact]
    public async Task Resolve_WithMatchingRedirect_ReturnsTarget()
    {
        var context = CreateContext();
        await context.SlugRedirects.AddAsync(new Domain.Entities.SlugRedirect
        {
            EntityType = SlugEntityType.Product,
            OldSlug = "cashmere-sweater-old",
            NewSlug = "cashmere-crew-neck-sweater",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var target = await service.ResolveAsync(SlugEntityType.Product, "cashmere-sweater-old");

        Assert.Equal("cashmere-crew-neck-sweater", target);
    }

    [Fact]
    public async Task Resolve_WithoutMatchingRedirect_ReturnsNull()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var target = await service.ResolveAsync(SlugEntityType.Product, "does-not-exist");

        Assert.Null(target);
    }

    [Fact]
    public async Task Resolve_IsCaseInsensitiveOnSlug()
    {
        var context = CreateContext();
        await context.SlugRedirects.AddAsync(new Domain.Entities.SlugRedirect
        {
            EntityType = SlugEntityType.Category,
            OldSlug = "Outerwear",
            NewSlug = "coats",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var target = await service.ResolveAsync(SlugEntityType.Category, "outerwear");

        Assert.Equal("coats", target);
    }

    [Fact]
    public async Task Resolve_IsScopedToEntityType()
    {
        var context = CreateContext();
        await context.SlugRedirects.AddAsync(new Domain.Entities.SlugRedirect
        {
            EntityType = SlugEntityType.Product,
            OldSlug = "everlane",
            NewSlug = "everlane-2026",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Assert.Equal("everlane-2026", await service.ResolveAsync(SlugEntityType.Product, "everlane"));
        Assert.Null(await service.ResolveAsync(SlugEntityType.Brand, "everlane"));
    }

    [Fact]
    public async Task Resolve_ReturnsNullWhenTargetIsEmpty()
    {
        var context = CreateContext();
        await context.SlugRedirects.AddAsync(new Domain.Entities.SlugRedirect
        {
            EntityType = SlugEntityType.Page,
            OldSlug = "old-page",
            NewSlug = string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var target = await service.ResolveAsync(SlugEntityType.Page, "old-page");

        Assert.Null(target);
    }

    // ---- Mutations ----

    [Fact]
    public async Task AddOrUpdate_CreatesNewRedirect()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.AddOrUpdateAsync(new SlugRedirectRequest(
            SlugEntityType.Product,
            "old-slug",
            "new-slug"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await context.SlugRedirects.CountAsync());
        var created = await context.SlugRedirects.SingleAsync();
        Assert.Equal("new-slug", created.NewSlug);
    }

    [Fact]
    public async Task AddOrUpdate_UpdatesExistingRedirectInsteadOfDuplicating()
    {
        var context = CreateContext();
        var service = CreateService(context);

        await service.AddOrUpdateAsync(new SlugRedirectRequest(SlugEntityType.Product, "old-slug", "first"));
        await service.AddOrUpdateAsync(new SlugRedirectRequest(SlugEntityType.Product, "old-slug", "second"));

        Assert.Equal(1, await context.SlugRedirects.CountAsync());
        var updated = await context.SlugRedirects.SingleAsync();
        Assert.Equal("second", updated.NewSlug);
    }

    [Fact]
    public async Task AddOrUpdate_RejectsSlugWithSlash()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.AddOrUpdateAsync(new SlugRedirectRequest(
            SlugEntityType.Product,
            "bad/slug",
            "good-slug"));

        Assert.False(result.IsSuccess);
        Assert.Contains("slug", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remove_DeletesRedirect()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var result = await service.AddOrUpdateAsync(new SlugRedirectRequest(SlugEntityType.Brand, "old-brand", "new-brand"));

        var remove = await service.RemoveAsync(result.Value);
        Assert.True(remove.IsSuccess);
        Assert.Equal(0, await context.SlugRedirects.CountAsync());
    }

    [Fact]
    public async Task Remove_UnknownId_ReturnsFailure()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.RemoveAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
    }
}
