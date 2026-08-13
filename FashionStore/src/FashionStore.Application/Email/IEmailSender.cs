namespace FashionStore.Application.Email;

/// <summary>A fully resolved outbound message ready for a provider to deliver.</summary>
public sealed record EmailOutboundMessage(
    string ToEmail,
    string Subject,
    string BodyHtml,
    string? AttachmentFileName = null,
    string? AttachmentContentType = null,
    byte[]? AttachmentBytes = null);

/// <summary>Outcome of a single provider delivery attempt.</summary>
public sealed record EmailSendResult(bool Success, string? SanitizedError = null);

/// <summary>
/// Transport abstraction over the concrete email providers (development sink,
/// SMTP and the future HTTP API providers). The sender job calls this after the
/// outbox transaction commits; no business code sends email directly.
/// </summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailOutboundMessage message, CancellationToken cancellationToken = default);
}
