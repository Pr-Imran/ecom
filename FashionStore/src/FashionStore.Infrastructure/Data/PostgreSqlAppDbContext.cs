using Microsoft.EntityFrameworkCore;

namespace FashionStore.Infrastructure.Data;

/// <summary>
/// PostgreSQL migration host. The application always resolves
/// <see cref="AppDbContext"/> at runtime (the provider is chosen from
/// Database:Provider), but EF Core needs a distinct context type so the Postgres
/// migration chain and model snapshot keep their own history, parallel to the
/// SQL Server chain, without disturbing it. The shared <see cref="AppDbContext"/>
/// model remaps SQL Server "datetime2" columns to Postgres "timestamp with time
/// zone" when the Npgsql provider is active, so this context produces a valid
/// Postgres schema.
/// </summary>
public class PostgreSqlAppDbContext : AppDbContext
{
    public PostgreSqlAppDbContext(DbContextOptions<PostgreSqlAppDbContext> options)
        : base(options)
    {
    }
}
