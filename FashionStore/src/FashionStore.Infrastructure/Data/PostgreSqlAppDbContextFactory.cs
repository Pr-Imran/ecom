using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FashionStore.Infrastructure.Data;

/// <summary>
/// Design-time factory used to generate and apply PostgreSQL migrations. The
/// connection string is read from configuration/environment (Database__Provider
/// and Database__ConnectionString); a placeholder is enough for "migrations add"
/// because the schema is derived from the model, not a live database. Use with
/// e.g. <c>dotnet ef migrations add InitialPostgres --context PostgreSqlAppDbContext</c>.
/// </summary>
public class PostgreSqlAppDbContextFactory : IDesignTimeDbContextFactory<PostgreSqlAppDbContext>
{
    public PostgreSqlAppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString")
            ?? "Host=localhost;Port=5432;Database=fashionstore;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory"))
            .Options;

        return new PostgreSqlAppDbContext(options);
    }
}
