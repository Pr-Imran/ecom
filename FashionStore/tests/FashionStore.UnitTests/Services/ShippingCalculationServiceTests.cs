using FashionStore.Application.DTOs.Shipping;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FashionStore.UnitTests.Services;

public class ShippingCalculationServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"shipping-calc-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static ShippingCalculationService CreateService(AppDbContext context)
    {
        return new ShippingCalculationService(context, NullLogger<ShippingCalculationService>.Instance);
    }

    private static ShippingLineInput Line(Product product, int quantity = 1)
    {
        return new ShippingLineInput(product.Id, Guid.NewGuid(), quantity);
    }

    private static Category CreateCategory(string name = "Apparel")
    {
        return new Category { Name = name, Slug = name.ToLowerInvariant(), DisplayOrder = 0, IsActive = true };
    }

    private static Product CreateProduct(Guid categoryId, string name = "Tee", decimal? weight = 0.5m)
    {
        return new Product
        {
            Name = name,
            Slug = name.ToLowerInvariant(),
            BaseSku = "SKU" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            BasePrice = 25m,
            CategoryId = categoryId,
            Weight = weight,
            IsActive = true,
            PublishedAtUtc = DateTime.UtcNow
        };
    }

    private static ShippingMethod CreateMethod(
        string code = "STANDARD",
        bool isActive = true,
        decimal? freeShippingThreshold = null,
        decimal? maxPackageWeight = null,
        int displayOrder = 0)
    {
        var now = DateTime.UtcNow;
        return new ShippingMethod
        {
            Code = code,
            Name = code,
            Type = ShippingMethodType.Standard,
            IsActive = isActive,
            DisplayOrder = displayOrder,
            EstimatedMinDays = 3,
            EstimatedMaxDays = 5,
            SupportsCashOnDelivery = true,
            RequiresShippingAddress = true,
            FreeShippingThreshold = freeShippingThreshold,
            MaxPackageWeight = maxPackageWeight,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static ShippingRate CreateRate(
        decimal amount,
        Guid? zoneId = null,
        string? cityName = null,
        ShippingRateType rateType = ShippingRateType.Flat,
        decimal? minWeightKg = null,
        decimal? maxWeightKg = null,
        decimal? minOrderAmount = null,
        int priority = 0)
    {
        var now = DateTime.UtcNow;
        return new ShippingRate
        {
            Name = "Delivery",
            RateType = rateType,
            Amount = amount,
            ShippingZoneId = zoneId,
            CityName = cityName,
            MinWeightKg = minWeightKg,
            MaxWeightKg = maxWeightKg,
            MinOrderAmount = minOrderAmount,
            Priority = priority,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static async Task<ShippingZone> SeedZoneAsync(
        AppDbContext context,
        string name = "North America",
        string[]? countries = null,
        string[]? cities = null)
    {
        var now = DateTime.UtcNow;
        var zone = new ShippingZone
        {
            Name = name,
            IsActive = true,
            DisplayOrder = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        if (countries != null)
        {
            foreach (var code in countries)
            {
                zone.Countries.Add(new ShippingZoneCountry { CountryCode = code });
            }
        }
        if (cities != null)
        {
            foreach (var city in cities)
            {
                zone.Cities.Add(new ShippingZoneCity { CityName = city, NormalizedCityName = city.ToUpperInvariant() });
            }
        }
        context.ShippingZones.Add(zone);
        await context.SaveChangesAsync();
        return zone;
    }

    [Fact]
    public async Task Quote_MatchesCountryZone_AndFallsBackToGlobalRate()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var zone = await SeedZoneAsync(context, countries: new[] { "US" });

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 10m, zoneId: zone.Id));
        method.Rates.Add(CreateRate(amount: 5m, priority: 0));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var usQuote = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(product) }), CancellationToken.None);
        var us = Assert.Single(usQuote.Quotes);
        Assert.True(usQuote.IsSupported);
        Assert.True(us.IsAvailable);
        Assert.Equal(10m, us.Price);

        var caQuote = await service.QuoteAsync(new ShippingCalculationInput("CA", null, null, null, 50m, new[] { Line(product) }), CancellationToken.None);
        var ca = Assert.Single(caQuote.Quotes);
        Assert.True(caQuote.IsSupported);
        Assert.True(ca.IsAvailable);
        Assert.Equal(5m, ca.Price);
    }

    [Fact]
    public async Task Quote_CityScope_MatchesCaseInsensitively()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var zone = await SeedZoneAsync(context, countries: new[] { "US" });
        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 8m, zoneId: zone.Id, cityName: "New York"));
        method.Rates.Add(CreateRate(amount: 12m, zoneId: zone.Id));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("US", "new york", null, null, 50m, new[] { Line(product) }), CancellationToken.None);
        var option = Assert.Single(quote.Quotes);
        Assert.True(option.IsAvailable);
        Assert.Equal(8m, option.Price);
    }

    [Fact]
    public async Task Quote_WeightBand_SelectsCorrectRate()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 5m, minWeightKg: 0m, maxWeightKg: 1m));
        method.Rates.Add(CreateRate(amount: 10m, minWeightKg: 1m, maxWeightKg: 5m));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id, weight: 0.5m);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var light = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(product, 1) }), CancellationToken.None);
        Assert.Equal(5m, Assert.Single(light.Quotes).Price);

        var heavy = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(product, 3) }), CancellationToken.None);
        Assert.Equal(10m, Assert.Single(heavy.Quotes).Price);
    }

    [Fact]
    public async Task Quote_PerUnitWeight_ScalesWithServerWeight()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 3m, rateType: ShippingRateType.PerUnitWeight));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id, weight: 2m);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(product, 2) }), CancellationToken.None);
        Assert.Equal(12m, Assert.Single(quote.Quotes).Price);
    }

    [Fact]
    public async Task Quote_FreeShippingThreshold_AppliesWhenSubtotalReaches()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod(freeShippingThreshold: 100m);
        method.Rates.Add(CreateRate(amount: 9.99m));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var below = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 80m, new[] { Line(product) }), CancellationToken.None);
        var belowOption = Assert.Single(below.Quotes);
        Assert.False(belowOption.IsFree);
        Assert.Equal(9.99m, belowOption.Price);
        Assert.Equal(20m, belowOption.RemainingForFreeShipping);

        var above = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 120m, new[] { Line(product) }), CancellationToken.None);
        var aboveOption = Assert.Single(above.Quotes);
        Assert.True(aboveOption.IsFree);
        Assert.Equal(0m, aboveOption.Price);
    }

    [Fact]
    public async Task Quote_CouponFreeShipping_MakesMethodFreeRegardlessOfSubtotal()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod(freeShippingThreshold: 200m);
        method.Rates.Add(CreateRate(amount: 9.99m));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 20m, new[] { Line(product) }, CouponFreeShipping: true), CancellationToken.None);
        var option = Assert.Single(quote.Quotes);
        Assert.True(option.IsFree);
        Assert.Equal(0m, option.Price);
    }

    [Fact]
    public async Task Quote_InactiveMethod_IsNotReturned()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var active = CreateMethod(code: "STANDARD", isActive: true);
        active.Rates.Add(CreateRate(amount: 5m));
        context.ShippingMethods.Add(active);

        var inactive = CreateMethod(code: "OLD", isActive: false, displayOrder: 1);
        inactive.Rates.Add(CreateRate(amount: 2m));
        context.ShippingMethods.Add(inactive);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(product) }), CancellationToken.None);
        var option = Assert.Single(quote.Quotes);
        Assert.Equal("STANDARD", option.Code);
    }

    [Fact]
    public async Task Quote_UnsupportedCountry_ReturnsUnsupported()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 5m));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("ZZ", null, null, null, 50m, new[] { Line(product) }), CancellationToken.None);
        Assert.False(quote.IsSupported);
        Assert.Contains("do not recognize", quote.UnsupportedReason);
    }

    [Fact]
    public async Task Quote_ValidCountryWithNoCoverage_ReturnsUnsupported()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var zone = await SeedZoneAsync(context, countries: new[] { "US" });
        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 5m, zoneId: zone.Id));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("GB", null, null, null, 50m, new[] { Line(product) }), CancellationToken.None);
        Assert.False(quote.IsSupported);
        Assert.Contains("do not currently deliver", quote.UnsupportedReason);
    }

    [Fact]
    public async Task Quote_ExcludedProduct_MethodUnavailable()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var category = CreateCategory();
        context.Categories.Add(category);
        var excluded = CreateProduct(category.Id, name: "Fragile");
        var normal = CreateProduct(category.Id, name: "Tee");
        context.Products.AddRange(excluded, normal);

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 5m));
        method.ProductRestrictions.Add(new ShippingMethodProduct
        {
            ShippingMethodId = method.Id,
            ProductId = excluded.Id,
            IsExclusion = true
        });
        context.ShippingMethods.Add(method);
        await context.SaveChangesAsync();

        var withExcluded = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(excluded) }), CancellationToken.None);
        Assert.False(Assert.Single(withExcluded.Quotes).IsAvailable);

        var withoutExcluded = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(normal) }), CancellationToken.None);
        Assert.True(Assert.Single(withoutExcluded.Quotes).IsAvailable);
    }

    [Fact]
    public async Task Quote_IncludedProduct_MethodOnlyAppliesToSelectItems()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var category = CreateCategory();
        context.Categories.Add(category);
        var special = CreateProduct(category.Id, name: "Special");
        var normal = CreateProduct(category.Id, name: "Tee");
        context.Products.AddRange(special, normal);

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 5m));
        method.ProductRestrictions.Add(new ShippingMethodProduct
        {
            ShippingMethodId = method.Id,
            ProductId = special.Id,
            IsExclusion = false
        });
        context.ShippingMethods.Add(method);
        await context.SaveChangesAsync();

        var withoutSpecial = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(normal) }), CancellationToken.None);
        var without = Assert.Single(withoutSpecial.Quotes);
        Assert.False(without.IsAvailable);
        Assert.Contains("only available for select items", without.UnavailableReason);

        var withSpecial = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(special) }), CancellationToken.None);
        Assert.True(Assert.Single(withSpecial.Quotes).IsAvailable);
    }

    [Fact]
    public async Task Quote_MaxPackageWeight_MethodUnavailable()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod(maxPackageWeight: 1m);
        method.Rates.Add(CreateRate(amount: 5m));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var heavy = CreateProduct(category.Id, name: "Heavy", weight: 2m);
        context.Products.Add(heavy);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(heavy) }), CancellationToken.None);
        Assert.False(Assert.Single(quote.Quotes).IsAvailable);
    }

    [Fact]
    public async Task Quote_ActiveBlackout_MethodUnavailable()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 5m));
        context.ShippingMethods.Add(method);

        var now = DateTime.UtcNow;
        context.DeliveryBlackouts.Add(new DeliveryBlackout
        {
            ShippingMethodId = method.Id,
            StartAtUtc = now.AddHours(-1),
            EndAtUtc = now.AddHours(1),
            Reason = "Carrier outage",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var quote = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 50m, new[] { Line(product) }), CancellationToken.None);
        Assert.False(Assert.Single(quote.Quotes).IsAvailable);
    }

    [Fact]
    public async Task Quote_CalculationConsistency_PriceMatchesConfiguredRate()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var method = CreateMethod();
        method.Rates.Add(CreateRate(amount: 7.5m));
        context.ShippingMethods.Add(method);

        var category = CreateCategory();
        context.Categories.Add(category);
        var product = CreateProduct(category.Id, weight: 0.5m);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // The subtotal and a wrong client weight must not change the server price.
        var quote = await service.QuoteAsync(new ShippingCalculationInput("US", null, null, null, 999m, new[] { Line(product, 5) }), CancellationToken.None);
        Assert.Equal(7.5m, Assert.Single(quote.Quotes).Price);
    }
}
