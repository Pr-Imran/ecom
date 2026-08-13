using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single outbound email message queued for delivery by the background job
/// system. The row is written in the same database transaction as the business
/// change that triggered it (an outbox), so important email is never sent before
/// that transaction commits — if the transaction rolls back the email disappears
/// with it. The table doubles as the email log for administrators: every send
/// attempt, retry and outcome is recorded here.
/// </summary>
public class EmailMessage : AuditedEntity
{
    /// <summary>The primary recipient.</summary>
    [Required]
    [MaxLength(254)]
    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Recipient display name used in the greeting, when known.</summary>
    [MaxLength(200)]
    public string? RecipientName { get; set; }

    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>The fully rendered, responsive HTML body.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Razor template name used to produce the body, for the template preview.</summary>
    [MaxLength(100)]
    public string? TemplateName { get; set; }

    /// <summary>JSON payload (template model) kept so a failed email can be previewed or re-rendered.</summary>
    public string? TemplateDataJson { get; set; }

    /// <summary>
    /// Optional attachment produced lazily by the sender job (for example
    /// <c>InvoicePdf</c>). Heavy documents are generated in the background job,
    /// never in the request that queued the email.
    /// </summary>
    [MaxLength(50)]
    public string? AttachmentKind { get; set; }

    public EmailStatus Status { get; set; } = EmailStatus.Pending;

    /// <summary>How many delivery attempts have been made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Delivery retry cap for this message.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Earliest time the sender job may try this email again.</summary>
    [Column(TypeName = "datetime2")]
    public DateTime? NextAttemptAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? SentAtUtc { get; set; }

    /// <summary>Sanitized error from the last failed attempt (no credentials or secrets).</summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>
    /// Stable key used to prevent duplicate emails (for example
    /// <c>order-shipped:{orderId}</c>). Nullable; a filtered unique index enforces
    /// one row per key.
    /// </summary>
    [MaxLength(450)]
    public string? DeduplicationKey { get; set; }
}
