using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FashionStore.UnitTests.Emails;

public class EmailOutboxTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-email-outbox-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static EmailOutbox CreateOutbox(AppDbContext context, int maxAttempts = 5) =>
        new(
            context,
            new EmailSettings { MaxAttempts = maxAttempts, RetryBaseDelayMinutes = 5 },
            NullLogger<EmailOutbox>.Instance);

    private static QueuedEmailDraft Draft(
        string to = "jane@example.com",
        string? dedupKey = "order-shipped:abc",
        string subject = "Your order has shipped") =>
        new(
            to,
            "Jane Doe",
            subject,
            "<html><body>Hello</body></html>",
            "OrderShipped",
            null,
            null,
            dedupKey);

    [Fact]
    public async Task Enqueue_CreatesPendingRowWithDefaults()
    {
        await using var context = CreateContext();
        var outbox = CreateOutbox(context);

        await outbox.EnqueueAsync(Draft(), CancellationToken.None);

        var row = Assert.Single(context.EmailMessages);
        Assert.Equal("jane@example.com", row.ToEmail);
        Assert.Equal("Jane Doe", row.RecipientName);
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(5, row.MaxAttempts);
        Assert.NotNull(row.NextAttemptAtUtc);
        Assert.Null(row.SentAtUtc);
        Assert.Equal("<html><body>Hello</body></html>", row.BodyHtml);
        Assert.Equal("OrderShipped", row.TemplateName);
        Assert.Equal("order-shipped:abc", row.DeduplicationKey);
    }

    [Fact]
    public async Task Enqueue_SameDeduplicationKey_SecondCallIsSkipped()
    {
        await using var context = CreateContext();
        var outbox = CreateOutbox(context);

        await outbox.EnqueueAsync(Draft(dedupKey: "order-shipped:abc"), CancellationToken.None);
        await outbox.EnqueueAsync(Draft(dedupKey: "order-shipped:abc", subject: "duplicate"), CancellationToken.None);

        var row = Assert.Single(context.EmailMessages);
        Assert.Equal("Your order has shipped", row.Subject);
    }

    [Fact]
    public async Task Enqueue_NullDeduplicationKey_AllowsDuplicates()
    {
        await using var context = CreateContext();
        var outbox = CreateOutbox(context);

        await outbox.EnqueueAsync(Draft(dedupKey: null), CancellationToken.None);
        await outbox.EnqueueAsync(Draft(dedupKey: null), CancellationToken.None);

        Assert.Equal(2, context.EmailMessages.Count());
    }

    [Fact]
    public async Task Enqueue_InvalidRecipient_IsSkipped()
    {
        await using var context = CreateContext();
        var outbox = CreateOutbox(context);

        await outbox.EnqueueAsync(Draft(to: ""), CancellationToken.None);
        await outbox.EnqueueAsync(Draft(to: "not-an-email"), CancellationToken.None);
        await outbox.EnqueueAsync(Draft(to: "  "), CancellationToken.None);

        Assert.Empty(context.EmailMessages);
    }

    [Fact]
    public async Task Enqueue_TrimsRecipientAndUsesSettingsMaxAttempts()
    {
        await using var context = CreateContext();
        var outbox = CreateOutbox(context, maxAttempts: 3);

        await outbox.EnqueueAsync(Draft(to: "  jane@example.com  "), CancellationToken.None);

        var row = Assert.Single(context.EmailMessages);
        Assert.Equal("jane@example.com", row.ToEmail);
        Assert.Equal(3, row.MaxAttempts);
    }
}
