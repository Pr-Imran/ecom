using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Emails;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FashionStore.UnitTests.Emails;

/// <summary>
/// Verifies the outbox reliability requirement: an email row is written inside the
/// caller's database transaction, so it is never sent before that transaction
/// commits — a rollback removes the queued email with it. Uses a real SQLite
/// in-memory database because EF Core's InMemory provider does not support
/// transactions.
/// </summary>
public class TransactionTimingTests
{
    private static (SqliteConnection Connection, AppDbContext Context) CreateSqliteContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return (connection, context);
    }

    private static EmailOutbox CreateOutbox(AppDbContext context) =>
        new(context, new EmailSettings { MaxAttempts = 5 }, NullLogger<EmailOutbox>.Instance);

    private static QueuedEmailDraft Draft() =>
        new(
            "jane@example.com",
            "Jane Doe",
            "Your order has shipped",
            "<html><body>Hello</body></html>",
            "OrderShipped",
            null,
            null,
            "order-shipped:tx-test");

    [Fact]
    public async Task RolledBackTransaction_RemovesQueuedEmail()
    {
        var (connection, context) = CreateSqliteContext();
        await using var scope = context;

        await using var tx = await context.Database.BeginTransactionAsync();

        var outbox = CreateOutbox(context);
        await outbox.EnqueueAsync(Draft(), CancellationToken.None);

        Assert.Single(context.EmailMessages);

        await tx.RollbackAsync();

        Assert.Empty(context.EmailMessages);
    }

    [Fact]
    public async Task CommittedTransaction_PersistsQueuedEmail()
    {
        var (connection, context) = CreateSqliteContext();
        await using var scope = context;

        await using var tx = await context.Database.BeginTransactionAsync();

        var outbox = CreateOutbox(context);
        await outbox.EnqueueAsync(Draft(), CancellationToken.None);

        await tx.CommitAsync();

        var row = Assert.Single(context.EmailMessages);
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal("order-shipped:tx-test", row.DeduplicationKey);
    }

    [Fact]
    public async Task BusinessOperationThatFailsAfterEnqueue_RollsBackEmailWithIt()
    {
        var (connection, context) = CreateSqliteContext();
        await using var scope = context;

        await using var tx = await context.Database.BeginTransactionAsync();

        var outbox = CreateOutbox(context);

        // Simulate a business operation: some work + an outbox write.
        await outbox.EnqueueAsync(Draft(), CancellationToken.None);
        context.Orders.Add(new FashionStore.Domain.Entities.Order
        {
            PublicOrderNumber = "ORD-ROLLBACK",
            CustomerName = "Jane",
            Currency = "USD",
            Subtotal = 100m,
            ShippingCharge = 0m,
            GrandTotal = 100m,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // The operation fails and the caller rolls back.
        await tx.RollbackAsync();

        Assert.Empty(context.EmailMessages);
        Assert.Empty(context.Orders);
    }
}
