using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// The payment record for an order. One payment exists per order and tracks the
/// money collected against the order's grand total. The payment never stores raw
/// card information: only the provider code, the provider transaction id, the
/// internal id and masked request/response metadata are retained so the flow can
/// be audited without exposing sensitive data. A payment is only ever marked paid
/// from a verified provider callback or webhook, never from a browser redirect.
/// </summary>
public class Payment : Entity
{
    public Guid OrderId { get; set; }
    public virtual Order? Order { get; set; }

    /// <summary>Stable provider key ("cod", "card", "mfs", "bank").</summary>
    [Required]
    [MaxLength(50)]
    public string ProviderCode { get; set; } = string.Empty;

    /// <summary>Payment method code from the checkout catalog.</summary>
    [Required]
    [MaxLength(50)]
    public string PaymentMethodCode { get; set; } = string.Empty;

    /// <summary>External transaction id assigned by the payment provider.</summary>
    [MaxLength(128)]
    public string? ProviderTransactionId { get; set; }

    /// <summary>Idempotency key carried on the payment initiation request.</summary>
    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    public PaymentState State { get; set; } = PaymentState.Pending;

    [MaxLength(50)]
    public string? FailureCode { get; set; }

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    /// <summary>Masked request metadata sent to the provider, serialized as JSON.</summary>
    public string? RequestMetadata { get; set; }

    /// <summary>Masked response metadata received from the provider, serialized as JSON.</summary>
    public string? ResponseMetadata { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? InitiatedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? CompletedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? FailedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? ExpiresAtUtc { get; set; }

    public virtual ICollection<PaymentAttempt> Attempts { get; set; } = new List<PaymentAttempt>();
    public virtual ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
    public virtual ICollection<PaymentRefundRecord> Refunds { get; set; } = new List<PaymentRefundRecord>();
}
