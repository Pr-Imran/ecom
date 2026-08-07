using FashionStore.Application.DTOs.Shipping;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FashionStore.UnitTests.Services;

public class ShippingServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"shipping-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static ShippingService CreateService(AppDbContext context)
    {
        return new ShippingService(context, NullLogger<ShippingService>.Instance);
    }

    private static CreateShippingMethodRequest CreateMethodRequest(
        string code = "STANDARD",
        ShippingMethodType type = ShippingMethodType.Standard,
        IReadOnlyList<ShippingMethodProductRestrictionDto>? productRestrictions = null,
        IReadOnlyList<ShippingMethodCategoryRestrictionDto>? categoryRestrictions = null)
    {
        return new CreateShippingMethodRequest(
            Code: code,
            Name: "Standard Delivery",
            Description: null,
            Type: type,
            EstimatedMinDays: 3,
            EstimatedMaxDays: 5,
            SupportsCashOnDelivery: true,
            RequiresShippingAddress: true,
            FreeShippingThreshold: null,
            MaxPackageWeight: null,
            PickupInstructions: null,
            ProductRestrictions: productRestrictions ?? Array.Empty<ShippingMethodProductRestrictionDto>(),
            CategoryRestrictions: categoryRestrictions ?? Array.Empty<ShippingMethodCategoryRestrictionDto>());
    }

    [Fact]
    public async Task CreateMethod_NormalizesCodeToUpperCase()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = await service.CreateMethodAsync(CreateMethodRequest(code: "standard"), CancellationToken.None);

        Assert.Equal("STANDARD", method.Code);
        var stored = await context.ShippingMethods.SingleAsync();
        Assert.Equal("STANDARD", stored.Code);
    }

    [Fact]
    public async Task CreateMethod_DuplicateCode_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await service.CreateMethodAsync(CreateMethodRequest(code: "STANDARD"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateMethodAsync(CreateMethodRequest(code: "standard"), CancellationToken.None));
    }

    [Fact]
    public async Task CreateMethod_PersistsProductAndCategoryRestrictions()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var request = CreateMethodRequest(
            productRestrictions: new[]
            {
                new ShippingMethodProductRestrictionDto(productId, false),
                new ShippingMethodProductRestrictionDto(Guid.NewGuid(), true)
            },
            categoryRestrictions: new[]
            {
                new ShippingMethodCategoryRestrictionDto(categoryId, false)
            });

        var method = await service.CreateMethodAsync(request, CancellationToken.None);

        Assert.Equal(2, method.ProductRestrictions.Count);
        Assert.Single(method.CategoryRestrictions);
        Assert.Equal(2, await context.ShippingMethodProducts.CountAsync());
        Assert.Equal(1, await context.ShippingMethodCategories.CountAsync());
    }

    [Fact]
    public async Task CreateMethod_InvalidEstimatedDays_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var request = CreateMethodRequest(code: "STANDARD") with { EstimatedMaxDays = 1, EstimatedMinDays = 5 };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateMethodAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMethod_ReplacesRestrictionsAtomically()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var firstProduct = Guid.NewGuid();
        var secondProduct = Guid.NewGuid();

        var created = await service.CreateMethodAsync(
            CreateMethodRequest(productRestrictions: new[] { new ShippingMethodProductRestrictionDto(firstProduct, false) }),
            CancellationToken.None);

        var updated = await service.UpdateMethodAsync(
            created.Id,
            new UpdateShippingMethodRequest(
                Id: created.Id,
                Code: created.Code,
                Name: "Renamed",
                Description: null,
                Type: ShippingMethodType.Express,
                IsActive: true,
                DisplayOrder: 1,
                EstimatedMinDays: 2,
                EstimatedMaxDays: 4,
                SupportsCashOnDelivery: false,
                RequiresShippingAddress: true,
                FreeShippingThreshold: null,
                MaxPackageWeight: null,
                PickupInstructions: null,
                ProductRestrictions: new[] { new ShippingMethodProductRestrictionDto(secondProduct, false) },
                CategoryRestrictions: Array.Empty<ShippingMethodCategoryRestrictionDto>()),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(1, updated.DisplayOrder);

        var remaining = await context.ShippingMethodProducts.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(secondProduct, remaining[0].ProductId);
    }

    [Fact]
    public async Task SetMethodActive_TogglesState()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var created = await service.CreateMethodAsync(CreateMethodRequest(), CancellationToken.None);

        await service.SetMethodActiveAsync(created.Id, false, CancellationToken.None);
        Assert.False((await context.ShippingMethods.SingleAsync()).IsActive);

        await service.SetMethodActiveAsync(created.Id, true, CancellationToken.None);
        Assert.True((await context.ShippingMethods.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task ReorderMethods_AssignsDisplayOrder()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var first = await service.CreateMethodAsync(CreateMethodRequest(code: "A"), CancellationToken.None);
        var second = await service.CreateMethodAsync(CreateMethodRequest(code: "B"), CancellationToken.None);

        var reordered = await service.ReorderMethodsAsync(new[] { second.Id, first.Id }, CancellationToken.None);

        Assert.True(reordered);
        var methods = await context.ShippingMethods.OrderBy(m => m.DisplayOrder).ToListAsync();
        Assert.Equal(new[] { second.Id, first.Id }, methods.Select(m => m.Id));
    }

    [Fact]
    public async Task CreateZone_NormalizesCountriesAndCities()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var zone = await service.CreateZoneAsync(
            new CreateShippingZoneRequest("North America", null, 0, new[] { "us", " CA " }, new[] { " New York ", "new york" }),
            CancellationToken.None);

        Assert.Equal(new[] { "CA", "US" }, zone.Countries);
        Assert.Single(zone.Cities);
        Assert.Equal("New York", zone.Cities[0]);

        var city = await context.ShippingZoneCities.SingleAsync();
        Assert.Equal("NEW YORK", city.NormalizedCityName);
    }

    [Fact]
    public async Task CreateRate_InvalidWeightBand_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var method = await service.CreateMethodAsync(CreateMethodRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRateAsync(
                new CreateShippingRateRequest(
                    method.Id,
                    null,
                    null,
                    "Delivery",
                    ShippingRateType.Flat,
                    5m,
                    MinWeightKg: 10m,
                    MaxWeightKg: 1m,
                    MinOrderAmount: null,
                    Priority: 0),
                CancellationToken.None));
    }

    [Fact]
    public async Task DeleteZone_InUseByRate_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var method = await service.CreateMethodAsync(CreateMethodRequest(), CancellationToken.None);
        var zone = await service.CreateZoneAsync(
            new CreateShippingZoneRequest("US Zone", null, 0, new[] { "US" }, Array.Empty<string>()),
            CancellationToken.None);

        await service.CreateRateAsync(
            new CreateShippingRateRequest(method.Id, zone.Id, null, "Delivery", ShippingRateType.Flat, 5m, null, null, null, 0),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteZoneAsync(zone.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteZone_NotInUse_Deletes()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var zone = await service.CreateZoneAsync(
            new CreateShippingZoneRequest("US Zone", null, 0, new[] { "US" }, Array.Empty<string>()),
            CancellationToken.None);

        var deleted = await service.DeleteZoneAsync(zone.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Equal(0, await context.ShippingZones.CountAsync());
    }

    [Fact]
    public async Task CreateBlackout_EndBeforeStart_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var method = await service.CreateMethodAsync(CreateMethodRequest(), CancellationToken.None);
        var now = DateTime.UtcNow;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateBlackoutAsync(
                new CreateDeliveryBlackoutRequest(method.Id, now.AddHours(2), now.AddHours(1), "Invalid"),
                CancellationToken.None));
    }
}
