using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Test host factory that swaps the SQL Server-backed <see cref="AppDbContext"/>
/// for an in-memory provider and seeds minimal catalogue data so integration
/// tests can run without a live database.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly InMemoryDatabaseRoot SharedRoot = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
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
                options.UseInMemoryDatabase("fashionstore-integration", SharedRoot));

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeedData(db);
        });
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

        db.Categories.AddRange(category, footwear);
        db.Brands.Add(brand);
        db.Collections.Add(collection);
        db.ProductTags.Add(cashmereTag);
        db.ProductAttributes.AddRange(colour, size);
        db.ProductAttributeValues.AddRange(heatherGrey, sizeM);
        db.Products.AddRange(product, shoe);
        db.SaveChanges();

        db.ProductVariants.AddRange(
            new ProductVariant
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
            },
            new ProductVariant
            {
                ProductId = shoe.Id,
                Sku = "SH-3003-BLK-09",
                Price = 150.00m,
                IsActive = true,
                IsDefault = true,
                StockQuantity = 0,
                ReservedStock = 0
            });

        db.ProductImages.Add(new ProductImage
        {
            ProductId = product.Id,
            FileName = "sweater.jpg",
            IsMain = true,
            DisplayOrder = 0,
            ImageFormat = "jpeg",
            ContentType = "image/jpeg"
        });

        db.ProductReviews.AddRange(
            new ProductReview { ProductId = product.Id, Rating = 5, IsApproved = true },
            new ProductReview { ProductId = product.Id, Rating = 4, IsApproved = true });

        db.SaveChanges();
    }
}
