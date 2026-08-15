using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Web host factory backed by a real relational provider (in-memory SQLite) for
/// the Phase 28 dashboard/report integration tests. EF Core's InMemory provider
/// cannot translate the GroupBy projections used by the dashboard and report
/// services, so aggregation tests must run against a provider that supports them.
/// The <c>[Timestamp]</c> <c>uint[] RowVersion</c> columns are not supported by
/// SQLite, so concurrency-token value generation is neutralized, foreign keys are
/// disabled and decimal columns are stored as REAL.
/// </summary>
public class SqliteReportTestFactory : WebApplicationFactory<Program>
{
    /// <summary>Product id seeded by <see cref="SeedCatalog"/> for report fixtures.</summary>
    public static Guid SeededProductId { get; private set; } = Guid.NewGuid();

    private static readonly object Sync = new();
    private static SqliteConnection? _connection;
    private static bool _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The report suite performs far more logins than the production rate-limit
        // budget from a single client address, so rate limiting is disabled for
        // the test host.
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
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton(_ => SharedConnection());

            services.AddScoped<AppDbContext>(sp =>
            {
                var connection = sp.GetRequiredService<SqliteConnection>();
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(connection)
                    .Options;
                return new ReportTestAppDbContext(options);
            });

            lock (Sync)
            {
                if (_seeded)
                {
                    return;
                }

                var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                SeedCatalog(db);
                _seeded = true;
            }
        });
    }

    private static SqliteConnection SharedConnection()
    {
        lock (Sync)
        {
            if (_connection is null)
            {
                _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
                _connection.Open();
            }
        }

        return _connection;
    }

    private static void SeedCatalog(AppDbContext db)
    {
        if (db.Products.Any())
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
            Category = category,
            Brand = brand,
            BaseSku = "SW-1001",
            BasePrice = 128.00m,
            IsActive = true,
            CreatedAtUtc = now.AddDays(-1),
            UpdatedAtUtc = now
        };

        db.Products.Add(product);
        db.SaveChanges();

        SeededProductId = product.Id;
    }

    /// <summary>
    /// SQLite-specific model adjustments mirroring the unit-test SqliteTestContext:
    /// concurrency tokens are neutralized and decimal columns map to REAL so the
    /// GroupBy-based report projections translate.
    /// </summary>
    private sealed class ReportTestAppDbContext : AppDbContext
    {
        public ReportTestAppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties().Where(p => p.IsConcurrencyToken))
                {
                    property.IsConcurrencyToken = false;
                    property.ValueGenerated = ValueGenerated.Never;
                }

                foreach (var property in entityType.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
                {
                    property.SetValueConverter(new ValueConverter<decimal?, double?>(
                        v => v.HasValue ? (double)v.Value : null,
                        v => v.HasValue ? (decimal)v.Value : null));
                }
            }
        }
    }
}
