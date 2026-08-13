using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Application.DTOs.Invoices;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.BackgroundJobs;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Emails;

public class SendQueuedEmailsJobTests
{
    private static readonly EmailSettings Settings = new()
    {
        MaxAttempts = 5,
        RetryBaseDelayMinutes = 5
    };

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-email-job-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static EmailMessage SeedPending(AppDbContext context, int maxAttempts = 5, DateTime? nextAttempt = null, int attempts = 0)
    {
        var email = new EmailMessage
        {
            ToEmail = "jane@example.com",
            Subject = "Your order has shipped",
            BodyHtml = "<html><body>Hello</body></html>",
            Status = EmailStatus.Pending,
            AttemptCount = attempts,
            MaxAttempts = maxAttempts,
            NextAttemptAtUtc = nextAttempt ?? DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };
        context.EmailMessages.Add(email);
        context.SaveChanges();
        return email;
    }

    private static SendQueuedEmailsJob CreateJob(
        AppDbContext context,
        Mock<IEmailSender>? sender = null,
        Mock<IInvoiceService>? invoices = null) =>
        new(
            context,
            sender?.Object ?? MockSender().Object,
            invoices?.Object ?? MockInvoiceService().Object,
            Settings,
            NullLogger<SendQueuedEmailsJob>.Instance);

    private static Mock<IEmailSender> MockSender(bool success = true, string? error = null)
    {
        var mock = new Mock<IEmailSender>();
        mock.Setup(s => s.SendAsync(It.IsAny<EmailOutboundMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(success, error));
        return mock;
    }

    private static Mock<IInvoiceService> MockInvoiceService()
    {
        var invoice = new InvoiceDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-2026-000001",
            "ORD-2026-000001",
            1,
            DateTime.UtcNow,
            "USD",
            128m,
            0m,
            0m,
            9.99m,
            0m,
            137.99m,
            137.99m,
            0m,
            0m,
            "Sent",
            DateTime.UtcNow,
            null,
            false,
            "Jane Doe",
            "jane@example.com",
            null,
            "card",
            "Paid",
            "Standard Delivery",
            null,
            null,
            null,
            Array.Empty<InvoiceItemDto>(),
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<InvoiceRefundReferenceDto>());

        var mock = new Mock<IInvoiceService>();
        mock.Setup(s => s.EnsureForOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        mock.Setup(s => s.BuildPdfAsync(invoice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF"
        return mock;
    }

    [Fact]
    public async Task Execute_SuccessfulDelivery_MarksSent()
    {
        await using var context = CreateContext();
        var email = SeedPending(context);
        var sender = MockSender(success: true);
        var job = CreateJob(context, sender);

        var processed = await job.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var row = await context.EmailMessages.SingleAsync(e => e.Id == email.Id);
        Assert.Equal(EmailStatus.Sent, row.Status);
        Assert.NotNull(row.SentAtUtc);
        Assert.Null(row.LastError);
        Assert.Equal(0, row.AttemptCount);
    }

    [Fact]
    public async Task Execute_TransientFailure_RecordsErrorAndSchedulesRetry()
    {
        await using var context = CreateContext();
        var email = SeedPending(context, maxAttempts: 5);
        var sender = MockSender(success: false, error: "SMTP server refused connection");
        var job = CreateJob(context, sender);

        await job.ExecuteAsync(CancellationToken.None);

        var row = await context.EmailMessages.SingleAsync(e => e.Id == email.Id);
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("SMTP server refused connection", row.LastError);
        Assert.NotNull(row.NextAttemptAtUtc);
        Assert.True(row.NextAttemptAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Execute_AttemptsExhausted_MarksFailed()
    {
        await using var context = CreateContext();
        var email = SeedPending(context, maxAttempts: 3, attempts: 2);
        var sender = MockSender(success: false, error: "permanent failure");
        var job = CreateJob(context, sender);

        await job.ExecuteAsync(CancellationToken.None);

        var row = await context.EmailMessages.SingleAsync(e => e.Id == email.Id);
        Assert.Equal(EmailStatus.Failed, row.Status);
        Assert.Equal(3, row.AttemptCount);
        Assert.Null(row.NextAttemptAtUtc);
        Assert.Equal("permanent failure", row.LastError);
    }

    [Fact]
    public async Task Execute_RetryAfterBackoff_EventuallySends()
    {
        await using var context = CreateContext();
        var email = SeedPending(context, maxAttempts: 5, nextAttempt: DateTime.UtcNow.AddMinutes(-30), attempts: 1);
        var sender = MockSender(success: true);
        var job = CreateJob(context, sender);

        var processed = await job.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var row = await context.EmailMessages.SingleAsync(e => e.Id == email.Id);
        Assert.Equal(EmailStatus.Sent, row.Status);
    }

    [Fact]
    public async Task Execute_NotYetDue_IsSkipped()
    {
        await using var context = CreateContext();
        SeedPending(context, nextAttempt: DateTime.UtcNow.AddHours(2));
        var job = CreateJob(context, MockSender(success: true));

        var processed = await job.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Equal(EmailStatus.Pending, context.EmailMessages.Single().Status);
    }

    [Fact]
    public async Task Execute_StuckProcessing_IsRequeuedWithIncrementedAttempt()
    {
        await using var context = CreateContext();
        var email = new EmailMessage
        {
            ToEmail = "jane@example.com",
            Subject = "stuck",
            BodyHtml = "<html/>",
            Status = EmailStatus.Processing,
            AttemptCount = 0,
            MaxAttempts = 5,
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-45)
        };
        context.EmailMessages.Add(email);
        context.SaveChanges();

        var job = CreateJob(context, MockSender(success: true));
        await job.ExecuteAsync(CancellationToken.None);

        var row = await context.EmailMessages.SingleAsync(e => e.Id == email.Id);
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Execute_InvoiceAttachment_IsGeneratedByJobAndAttached()
    {
        await using var context = CreateContext();
        var orderId = Guid.NewGuid();
        var email = new EmailMessage
        {
            ToEmail = "jane@example.com",
            Subject = "Your invoice",
            BodyHtml = "<html/>",
            TemplateName = "Invoice",
            AttachmentKind = "InvoicePdf",
            TemplateDataJson = orderId.ToString(),
            Status = EmailStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };
        context.EmailMessages.Add(email);
        context.SaveChanges();

        var invoices = MockInvoiceService();
        var sender = new Mock<IEmailSender>();
        EmailOutboundMessage? delivered = null;
        sender.Setup(s => s.SendAsync(It.IsAny<EmailOutboundMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailOutboundMessage, CancellationToken>((m, _) => delivered = m)
            .ReturnsAsync(new EmailSendResult(true));

        var job = CreateJob(context, sender, invoices);
        await job.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(delivered);
        Assert.Equal("invoice-INV-2026-000001.pdf", delivered!.AttachmentFileName);
        Assert.Equal("application/pdf", delivered.AttachmentContentType);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, delivered.AttachmentBytes);

        var row = await context.EmailMessages.SingleAsync(e => e.Id == email.Id);
        Assert.Equal(EmailStatus.Sent, row.Status);
    }

    [Fact]
    public async Task Execute_CancelledToken_ThrowsBeforeSending()
    {
        await using var context = CreateContext();
        SeedPending(context);
        var sender = MockSender(success: true);
        var job = CreateJob(context, sender);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => job.ExecuteAsync(cts.Token));
    }
}
