using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// Immutable record of an incoming webhook from a payment provider. The raw
/// payload is stored masked (sensitive data filtered) so the event can be
/// audited without leaking credentials or card data. The provider event id is
/// unique per provider so a replayed event is detected and ignored.
/// </summary>
public class PaymentWebhookLog : Entity
{
    /// <summary>Stable provider key ("cod", "card", "mfs", "bank").</summary>
    [Required]
    [MaxLength(50)]
    public string ProviderCode { get; set; } = string.Empty;

    /// <summary>Provider-assigned event id; used for replay protection.</summary>
    [MaxLength(128)]
    public string? ProviderEventId { get; set; }

    public Guid? PaymentId { get; set; }
    public virtual Payment? Payment { get; set; }

    /// <summary>Outcome of verification/processing for this event.</summary>
    public PaymentWebhookStatus Status { get; set; }

    /// <summary>The raw body, stored masked (sensitive fields filtered).</summary>
    public string? RawPayload { get; set; }

    /// <summary>Signature received on the request, for auditability.</summary>
    [MaxLength(1024)]
    public string? Signature { get; set; }

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime ReceivedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? ProcessedAtUtc { get; set; }
}
