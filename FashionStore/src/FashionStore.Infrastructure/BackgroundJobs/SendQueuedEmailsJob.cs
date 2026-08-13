using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.BackgroundJobs;

/// <summary>
/// Durable email dispatcher. Picks up due outbox rows, generates any lazily-created
/// attachments (invoice PDFs are produced here, not in the request path), delivers
/// through the active provider and records the outcome. Failures are retried with
/// exponential backoff until <see cref="EmailMessage.MaxAttempts"/> is reached, and
/// errors are stored sanitized. Also reaps messages stuck in <see cref="EmailStatus.Processing"/>
/// from a crashed worker so they are retried.
/// </summary>
public sealed class SendQueuedEmailsJob
{
    private const int BatchSize = 50;
    private static readonly TimeSpan StuckProcessingThreshold = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _context;
    private readonly IEmailSender _sender;
    private readonly IInvoiceService _invoiceService;
    private readonly EmailSettings _settings;
    private readonly ILogger<SendQueuedEmailsJob> _logger;

    public SendQueuedEmailsJob(
        AppDbContext context,
        IEmailSender sender,
        IInvoiceService invoiceService,
        EmailSettings settings,
        ILogger<SendQueuedEmailsJob> logger)
    {
        _context = context;
        _sender = sender;
        _invoiceService = invoiceService;
        _settings = settings;
        _logger = logger;
    }

    /// <returns>The number of emails processed in this run.</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var stuck = await _context.EmailMessages
            .Where(e => e.Status == EmailStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var email in stuck)
        {
            if (!email.UpdatedAtUtc.HasValue || now - email.UpdatedAtUtc.Value > StuckProcessingThreshold)
            {
                email.Status = EmailStatus.Pending;
                email.AttemptCount++;
                email.NextAttemptAtUtc = now.AddMinutes(GetBackoffMinutes(email.AttemptCount));
                email.UpdatedAtUtc = now;
                _logger.LogWarning("Email {EmailId} was stuck in Processing and has been re-queued (attempt {Attempt})", email.Id, email.AttemptCount);
            }
        }

        if (stuck.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        var due = await _context.EmailMessages
            .Where(e => e.Status == EmailStatus.Pending
                && e.AttemptCount < e.MaxAttempts
                && (e.NextAttemptAtUtc == null || e.NextAttemptAtUtc <= now))
            .OrderBy(e => e.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var email in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            email.Status = EmailStatus.Processing;
            email.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                var message = await BuildOutboundMessageAsync(email, cancellationToken);
                var result = await _sender.SendAsync(message, cancellationToken);

                if (result.Success)
                {
                    email.Status = EmailStatus.Sent;
                    email.SentAtUtc = DateTime.UtcNow;
                    email.LastError = null;
                    email.NextAttemptAtUtc = null;
                    _logger.LogInformation("Email {EmailId} to {To} delivered", email.Id, email.ToEmail);
                }
                else
                {
                    RecordFailure(email, result.SanitizedError, DateTime.UtcNow);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email {EmailId} to {To} failed unexpectedly", email.Id, email.ToEmail);
                RecordFailure(email, SanitizeError(ex.Message), DateTime.UtcNow);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task<EmailOutboundMessage> BuildOutboundMessageAsync(EmailMessage email, CancellationToken cancellationToken)
    {
        string? attachmentName = null;
        string? attachmentType = null;
        byte[]? attachmentBytes = null;

        if (string.Equals(email.AttachmentKind, "InvoicePdf", StringComparison.OrdinalIgnoreCase))
        {
            if (Guid.TryParse(email.TemplateDataJson, out var orderId))
            {
                var invoice = await _invoiceService.EnsureForOrderAsync(orderId, cancellationToken);
                attachmentBytes = await _invoiceService.BuildPdfAsync(invoice, cancellationToken);
                attachmentName = $"invoice-{invoice.InvoiceNumber}.pdf";
                attachmentType = "application/pdf";
            }
        }

        return new EmailOutboundMessage(
            email.ToEmail,
            email.Subject,
            email.BodyHtml,
            attachmentName,
            attachmentType,
            attachmentBytes);
    }

    private void RecordFailure(EmailMessage email, string? error, DateTime now)
    {
        email.AttemptCount++;
        email.LastError = SanitizeError(error);

        if (email.AttemptCount >= email.MaxAttempts)
        {
            email.Status = EmailStatus.Failed;
            email.NextAttemptAtUtc = null;
            _logger.LogWarning("Email {EmailId} to {To} failed permanently after {Attempts} attempts", email.Id, email.ToEmail, email.AttemptCount);
        }
        else
        {
            email.Status = EmailStatus.Pending;
            email.NextAttemptAtUtc = now.AddMinutes(GetBackoffMinutes(email.AttemptCount));
            _logger.LogWarning("Email {EmailId} to {To} failed (attempt {Attempt}/{Max}); retrying at {Next}",
                email.Id, email.ToEmail, email.AttemptCount, email.MaxAttempts, email.NextAttemptAtUtc);
        }
    }

    private int GetBackoffMinutes(int attempt)
    {
        var baseDelay = _settings.RetryBaseDelayMinutes > 0 ? _settings.RetryBaseDelayMinutes : 5;
        var multiplier = Math.Min(1 << Math.Max(0, attempt - 1), 128);
        return Math.Min(baseDelay * multiplier, 720);
    }

    private static string? SanitizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Unknown delivery error.";
        }

        var flattened = error.Replace("\r", " ").Replace("\n", " ").Trim();
        return flattened.Length <= 1000 ? flattened : flattened[..1000];
    }
}
