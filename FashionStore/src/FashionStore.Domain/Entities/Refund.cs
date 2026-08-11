using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A refund issued against a return. The <see cref="ReferenceNumber"/> is a public
/// credit-note-ready reference shown on the invoice and to the customer. Gateway
/// refunds also produce a <see cref="PaymentRefundRecord"/> through the payment
/// pipeline; manual refunds never touch the gateway. The unique
/// <see cref="IdempotencyKey"/> guarantees a given refund decision is only ever
/// applied once.
/// </summary>
public class Refund : AuditedEntity
{
    public Guid ReturnRequestId { get; set; }
    public virtual ReturnRequest? ReturnRequest { get; set; }

    public Guid OrderId { get; set; }

    /// <summary>Public credit-note-ready reference (for example "RFN-...").</summary>
    [Required]
    [MaxLength(50)]
    public string ReferenceNumber { get; set; } = string.Empty;

    public RefundType Type { get; set; }

    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    /// <summary>True when the refund was executed through the payment gateway.</summary>
    public bool IsGatewayRefund { get; set; }

    /// <summary>External refund id assigned by the payment provider.</summary>
    [MaxLength(128)]
    public string? ProviderRefundId { get; set; }

    /// <summary>Idempotency key that prevents the same refund decision from being applied twice.</summary>
    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? FailureCode { get; set; }

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    /// <summary>Human-readable reason for the refund (for example "Return RMA-..." or "Shipping charge").</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    [MaxLength(450)]
    public string? InitiatedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? CompletedAtUtc { get; set; }

    public virtual ICollection<RefundTransaction> Transactions { get; set; } = new List<RefundTransaction>();
}
