using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Emails;

/// <summary>
/// Outbox writer for queued emails. Uses the same scoped <see cref="AppDbContext"/>
/// as the caller so the email row is created inside the caller's ambient
/// transaction; the background sender only picks it up after that transaction has
/// committed, which is what guarantees important email is never sent early.
/// </summary>
public sealed class EmailOutbox : IEmailOutbox
{
    private readonly AppDbContext _context;
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailOutbox> _logger;

    public EmailOutbox(AppDbContext context, EmailSettings settings, ILogger<EmailOutbox> logger)
    {
        _context = context;
        _settings = settings;
        _logger = logger;
    }

    public async Task EnqueueAsync(QueuedEmailDraft draft, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.ToEmail) || !draft.ToEmail.Contains('@', StringComparison.Ordinal))
        {
            _logger.LogWarning("Email to {To} was skipped because the recipient address is invalid", draft.ToEmail);
            return;
        }

        if (!string.IsNullOrWhiteSpace(draft.DeduplicationKey))
        {
            var exists = await _context.EmailMessages
                .AnyAsync(e => e.DeduplicationKey == draft.DeduplicationKey, cancellationToken);
            if (exists)
            {
                _logger.LogInformation("Duplicate email {DedupKey} skipped", draft.DeduplicationKey);
                return;
            }
        }

        var now = DateTime.UtcNow;
        var maxAttempts = _settings.MaxAttempts > 0 ? _settings.MaxAttempts : 5;

        _context.EmailMessages.Add(new EmailMessage
        {
            ToEmail = draft.ToEmail.Trim(),
            RecipientName = draft.RecipientName,
            Subject = draft.Subject,
            BodyHtml = draft.BodyHtml,
            TemplateName = draft.TemplateName,
            TemplateDataJson = draft.TemplateDataJson,
            AttachmentKind = draft.AttachmentKind,
            DeduplicationKey = string.IsNullOrWhiteSpace(draft.DeduplicationKey) ? null : draft.DeduplicationKey,
            Status = EmailStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            CreatedBy = draft.CreatedBy
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
