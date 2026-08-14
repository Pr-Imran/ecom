using FashionStore.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FashionStore.UnitTests.Reports;

/// <summary>
/// Builds an in-memory SQLite context for aggregation tests. EF Core's InMemory
/// provider cannot translate the GroupBy projections used by the dashboard and
/// report services, so a real relational provider is required. The
/// <c>[Timestamp]</c> <c>uint[] RowVersion</c> columns are not supported by
/// SQLite, so concurrency-token value generation is neutralized.
/// </summary>
internal static class SqliteTestContext
{
    public static AppDbContext Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new TestAppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class TestAppDbContext : AppDbContext
    {
        public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options)
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
