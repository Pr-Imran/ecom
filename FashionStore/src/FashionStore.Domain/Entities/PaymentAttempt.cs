using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single attempt to collect a payment for a <see cref="Payment"/>. A payment
/// may have several attempts (a card retry, an MFS re-pay). Each attempt carries
/// its own provider interaction, masked metadata and outcome.
/// </summary>
public class PaymentAttempt : Entity
{
    public Guid PaymentId { get; set; }
    public virtual Payment? Payment { get; set; }

    public int AttemptNumber { get; set; }

    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;

    /// <summary>External attempt/transaction id assigned by the provider.</summary>
    [MaxLength(128)]
    public string? ProviderTransactionId { get; set; }

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    /// <summary>Masked request metadata, serialized as JSON.</summary>
    public string? RequestMetadata { get; set; }

    /// <summary>Masked response metadata, serialized as JSON.</summary>
    public string? ResponseMetadata { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? CompletedAtUtc { get; set; }
}
