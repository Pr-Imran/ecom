using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Test host factory that swaps the SQL Server-backed <see cref="AppDbContext"/>
/// for an in-memory provider and seeds minimal catalogue data so integration
/// tests can run without a live database.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly object SeedLock = new();
    private readonly string _databaseName = $"fashionstore-integration-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The full integration suite performs far more than the production
        // login rate-limit budget from a single client address, so rate limiting
        // is disabled for the test host and exercised by dedicated unit tests
        // against the configured policies.
        builder.UseSetting("RateLimiting:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            // The test server runs over HTTP, so the production
            // CookieSecurePolicy.Always auth cookie would never be sent back and
            // every authenticated request would bounce to the login page. Relax
            // the secure policy for the test host only.
            services.PostConfigure<CookieAuthenticationOptions>(
                IdentityConstants.ApplicationScheme,
                options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);

            var descriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            var serviceProvider = services.BuildServiceProvider();
            lock (SeedLock)
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                SeedData(db);

                // Registration assigns every new user the Customer role, so that
                // role must exist in the test host. Other roles are deliberately
                // NOT seeded here: several integration tests create a bare Admin
                // role themselves to assert that a role alone grants nothing and
                // that only explicit permission claims grant access.
                EnsureCustomerRole(scope.ServiceProvider);
            }
        });
    }

    private static void EnsureCustomerRole(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (roleManager.FindByNameAsync("Customer").GetAwaiter().GetResult() is null)
        {
            var created = roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Customer",
                NormalizedName = "CUSTOMER",
                Description = "Standard customer access",
                IsSystemRole = true,
                CreatedAtUtc = DateTime.UtcNow
            }).GetAwaiter().GetResult();
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed Customer role: {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }
        }
    }

    private static void SeedData(AppDbContext db)
    {
        if (db.Categories.Any())
        {
            return;
        }

        var now = DateTime.UtcNow;

        var category = new Category
        {
            Name = "Clothing",
            Slug = "clothing",
            IsActive = true,
            CreatedAtUtc = now
        };

        var footwear = new Category
        {
            Name = "Footwear",
            Slug = "footwear",
            IsActive = true,
            CreatedAtUtc = now
        };

        var brand = new Brand
        {
            Name = "Everlane",
            Slug = "everlane",
            IsActive = true,
            CreatedAtUtc = now
        };

        var collection = new Collection
        {
            Name = "Autumn Edit",
            Slug = "autumn-edit",
            IsActive = true,
            CreatedAtUtc = now
        };

        var colour = new ProductAttribute { Name = "Colour", Slug = "colour" };
        var heatherGrey = new ProductAttributeValue
        {
            Name = "Heather Grey",
            Slug = "heather-grey",
            HexColour = "#999999",
            ProductAttribute = colour,
            IsActive = true
        };

        var size = new ProductAttribute { Name = "Size", Slug = "size" };
        var sizeM = new ProductAttributeValue
        {
            Name = "M",
            Slug = "m",
            ProductAttribute = size,
            IsActive = true
        };

        var cashmereTag = new ProductTag { Name = "Cashmere", Slug = "cashmere" };

        var product = new Product
        {
            Name = "Cashmere Crew Neck Sweater",
            Slug = "cashmere-crew-neck-sweater",
            CategoryId = category.Id,
            BrandId = brand.Id,
            CollectionId = collection.Id,
            BaseSku = "SW-1001",
            BasePrice = 128.00m,
            CompareAtPrice = 160.00m,
            Material = "Cashmere",
            Gender = "Women",
            IsActive = true,
            IsNewArrival = true,
            IsFeatured = true,
            IsBestSeller = true,
            DisplayOrder = 1,
            PublishedAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-1),
            ProductTagMappings = new List<ProductTagMapping> { new() { ProductTag = cashmereTag } }
        };

        var shoe = new Product
        {
            Name = "Trail Running Shoe",
            Slug = "trail-running-shoe",
            CategoryId = footwear.Id,
            BrandId = brand.Id,
            BaseSku = "SH-3003",
            BasePrice = 150.00m,
            Material = "Synthetic",
            Gender = "Men",
            IsActive = true,
            IsBestSeller = true,
            DisplayOrder = 2,
            PublishedAtUtc = now.AddDays(-2),
            CreatedAtUtc = now.AddDays(-2)
        };

        var scarf = new Product
        {
            Name = "Merino Wool Scarf",
            Slug = "merino-wool-scarf",
            CategoryId = category.Id,
            BrandId = brand.Id,
            BaseSku = "AC-2001",
            BasePrice = 45.00m,
            CompareAtPrice = 60.00m,
            Material = "Wool",
            Gender = "Women",
            IsActive = true,
            IsFeatured = true,
            DisplayOrder = 3,
            PublishedAtUtc = now.AddDays(-3),
            CreatedAtUtc = now.AddDays(-3)
        };

        db.Categories.AddRange(category, footwear);
        db.Brands.Add(brand);
        db.Collections.Add(collection);
        db.ProductTags.Add(cashmereTag);
        db.ProductAttributes.AddRange(colour, size);
        db.ProductAttributeValues.AddRange(heatherGrey, sizeM);
        db.Products.AddRange(product, shoe, scarf);
        db.SaveChanges();

        var sweaterVariant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = "SW-1001-GREY-M",
            Price = 128.00m,
            IsActive = true,
            IsDefault = true,
            StockQuantity = 10,
            ReservedStock = 0,
            VariantAttributeValues = new List<ProductVariantAttributeValue>
            {
                new() { AttributeValue = heatherGrey },
                new() { AttributeValue = sizeM }
            }
        };
        var shoeVariant = new ProductVariant
        {
            ProductId = shoe.Id,
            Sku = "SH-3003-BLK-09",
            Price = 150.00m,
            IsActive = true,
            IsDefault = true,
            StockQuantity = 0,
            ReservedStock = 0
        };

        db.ProductVariants.AddRange(sweaterVariant, shoeVariant);

        db.ProductImages.Add(new ProductImage
        {
            ProductId = product.Id,
            FileName = "sweater.jpg",
            IsMain = true,
            DisplayOrder = 0,
            ImageFormat = "jpeg",
            ContentType = "image/jpeg"
        });

        var warehouse = new Warehouse
        {
            Name = "Main Warehouse",
            Code = "MAIN",
            Description = "Primary fulfilment warehouse",
            City = "New York",
            Country = "US",
            IsActive = true,
            IsDefault = true,
            DisplayOrder = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Warehouses.Add(warehouse);

        db.ProductReviews.AddRange(
            new ProductReview { ProductId = product.Id, Rating = 5, Status = ReviewStatus.Approved },
            new ProductReview { ProductId = product.Id, Rating = 4, Status = ReviewStatus.Approved });

        var usZone = new ShippingZone
        {
            Name = "United States",
            IsActive = true,
            DisplayOrder = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Countries = new List<ShippingZoneCountry>
            {
                new() { CountryCode = "US" }
            }
        };

        var globalZone = new ShippingZone
        {
            Name = "Rest of World",
            IsActive = true,
            DisplayOrder = 2,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var standard = new ShippingMethod
        {
            Code = "STANDARD",
            Name = "Standard Delivery",
            Type = FashionStore.Domain.Enums.ShippingMethodType.Standard,
            IsActive = true,
            DisplayOrder = 1,
            EstimatedMinDays = 3,
            EstimatedMaxDays = 5,
            SupportsCashOnDelivery = true,
            RequiresShippingAddress = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var express = new ShippingMethod
        {
            Code = "EXPRESS",
            Name = "Express Delivery",
            Type = FashionStore.Domain.Enums.ShippingMethodType.Express,
            IsActive = true,
            DisplayOrder = 2,
            EstimatedMinDays = 1,
            EstimatedMaxDays = 2,
            SupportsCashOnDelivery = true,
            RequiresShippingAddress = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.ShippingZones.AddRange(usZone, globalZone);
        db.ShippingMethods.AddRange(standard, express);
        db.ShippingRates.AddRange(
            new ShippingRate
            {
                ShippingMethodId = standard.Id,
                ShippingZoneId = usZone.Id,
                Name = "US Standard",
                RateType = FashionStore.Domain.Enums.ShippingRateType.Flat,
                Amount = 9.99m,
                Priority = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new ShippingRate
            {
                ShippingMethodId = standard.Id,
                ShippingZoneId = globalZone.Id,
                Name = "International Standard",
                RateType = FashionStore.Domain.Enums.ShippingRateType.Flat,
                Amount = 19.99m,
                Priority = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new ShippingRate
            {
                ShippingMethodId = express.Id,
                ShippingZoneId = usZone.Id,
                Name = "US Express",
                RateType = FashionStore.Domain.Enums.ShippingRateType.Flat,
                Amount = 24.99m,
                Priority = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        db.WarehouseStocks.AddRange(
            new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                ProductVariantId = sweaterVariant.Id,
                OnHandQuantity = 10,
                ReservedQuantity = 0,
                AllowBackorder = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                ProductVariantId = shoeVariant.Id,
                OnHandQuantity = 0,
                ReservedQuantity = 0,
                AllowBackorder = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        db.SaveChanges();
    }
}
