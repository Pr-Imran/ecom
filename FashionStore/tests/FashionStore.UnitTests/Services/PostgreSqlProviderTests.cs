using FashionStore.Application.Configuration;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.UnitTests.Services;

/// <summary>
/// Build-time smoke tests for the PostgreSQL provider support. These verify the
/// provider selection knob and the model remap (SQL Server "datetime2" columns
/// become Postgres "timestamp with time zone" when the Npgsql provider is active)
/// without needing a live database - the model is built in memory.
/// </summary>
public class PostgreSqlProviderTests
{
    [Fact]
    public void DatabaseSettings_DefaultProvider_IsSqlServer()
    {
        var settings = new DatabaseSettings();

        Assert.Equal("SqlServer", settings.Provider);
    }

    [Fact]
    public void NpgsqlModel_RemapsDatetime2Columns_ToTimestampWithTimeZone()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=fashionstore_test")
            .Options;

        using var context = new AppDbContext(options);
        var model = context.Model;

        var columnTypes = model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Select(p => p.GetColumnType())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        Assert.DoesNotContain(columnTypes, t => t == "datetime2");
        Assert.Contains(columnTypes, t => t == "timestamp with time zone");
    }

    [Fact]
    public void SqlServerModel_KeepsDatetime2Mapping()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=fashionstore_test;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var context = new AppDbContext(options);
        var model = context.Model;

        var columnTypes = model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Select(p => p.GetColumnType())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        Assert.Contains(columnTypes, t => t == "datetime2");
    }

    [Fact]
    public void DesignTimeFactory_CreatesPostgreSqlContext()
    {
        var factory = new PostgreSqlAppDbContextFactory();

        using var context = factory.CreateDbContext(Array.Empty<string>());

        Assert.NotNull(context);
        Assert.IsType<PostgreSqlAppDbContext>(context);
    }
}
