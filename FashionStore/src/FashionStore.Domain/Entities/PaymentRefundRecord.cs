using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A refund recorded against a <see cref="Payment"/>. Refunds reduce the amount
/// collected for the order and may be partial or full. No raw card information is
/// ever stored here.
/// </summary>
public class PaymentRefundRecord : Entity
{
    public Guid PaymentId { get; set; }
    public virtual Payment? Payment { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    /// <summary>External refund id assigned by the provider.</summary>
    [MaxLength(128)]
    public string? ProviderRefundId { get; set; }

    public bool Succeeded { get; set; }

    [MaxLength(50)]
    public string? FailureCode { get; set; }

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    /// <summary>Who or what initiated the refund (operator, return flow, ...).</summary>
    [MaxLength(200)]
    public string? InitiatedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? CompletedAtUtc { get; set; }
}
