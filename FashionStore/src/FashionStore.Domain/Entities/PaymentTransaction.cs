using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// An immutable audit entry recording a payment action (initiate, authorise,
/// capture, webhook, refund, expiry, ...). Every movement of money against a
/// payment is persisted here. Metadata is stored masked so sensitive fields never
/// reach the database.
/// </summary>
public class PaymentTransaction : Entity
{
    public Guid PaymentId { get; set; }
    public virtual Payment? Payment { get; set; }

    public PaymentTransactionType Type { get; set; }

    /// <summary>Stable provider key ("cod", "card", "mfs", "bank").</summary>
    [Required]
    [MaxLength(50)]
    public string ProviderCode { get; set; } = string.Empty;

    /// <summary>External transaction id assigned by the provider for this action.</summary>
    [MaxLength(128)]
    public string? ProviderTransactionId { get; set; }

    public bool Succeeded { get; set; }

    [MaxLength(50)]
    public string? ResultCode { get; set; }

    [MaxLength(500)]
    public string? ResultMessage { get; set; }

    /// <summary>Masked request metadata, serialized as JSON.</summary>
    public string? RequestMetadata { get; set; }

    /// <summary>Masked response metadata, serialized as JSON.</summary>
    public string? ResponseMetadata { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
