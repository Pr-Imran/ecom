using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
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
                options.UseInMemoryDatabase($"fashionstore-integration-{Guid.NewGuid()}"));

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

        var brand = new Brand
        {
            Name = "Everlane",
            Slug = "everlane",
            IsActive = true,
            CreatedAtUtc = now
        };

        var product = new Product
        {
            Name = "Cashmere Crew Neck Sweater",
            Slug = "cashmere-crew-neck-sweater",
            CategoryId = category.Id,
            BrandId = brand.Id,
            BaseSku = "SW-1001",
            BasePrice = 128.00m,
            CompareAtPrice = 160.00m,
            IsActive = true,
            IsNewArrival = true,
            IsFeatured = true,
            IsBestSeller = true,
            DisplayOrder = 1,
            PublishedAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-1)
        };

        db.Categories.Add(category);
        db.Brands.Add(brand);
        db.Products.Add(product);
        db.SaveChanges();
    }
}
