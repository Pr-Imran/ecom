namespace FashionStore.Application.Email;

/// <summary>A rendered email ready to be written to the outbox.</summary>
public sealed record QueuedEmailDraft(
    string ToEmail,
    string? RecipientName,
    string Subject,
    string BodyHtml,
    string? TemplateName,
    string? TemplateDataJson,
    string? AttachmentKind,
    string? DeduplicationKey,
    string? CreatedBy = null);

/// <summary>
/// Writes email messages to the durable outbox. Implementations must use the same
/// <c>AppDbContext</c> instance as the caller so the row participates in the
/// caller's ambient transaction — if that transaction rolls back the email is never
/// queued and therefore never sent.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>
    /// Queues an email for background delivery. When <paramref name="draft"/>
    /// carries a <see cref="QueuedEmailDraft.DeduplicationKey"/> that already exists
    /// the call is a no-op, preventing duplicate emails.
    /// </summary>
    Task EnqueueAsync(QueuedEmailDraft draft, CancellationToken cancellationToken = default);
}
