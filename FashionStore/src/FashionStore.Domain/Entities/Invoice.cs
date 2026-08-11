using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A document-level invoice produced for an order. Every financial field is copied
/// from the order's immutable placement snapshot at generation time and is never
/// re-derived from live catalogue data afterwards. The <see cref="InvoiceNumber"/>
/// is assigned by the concurrency-safe numbering sequence and is unique across the
/// store. Regeneration recomputes the amounts from the same order snapshot and
/// keeps the number stable so retries can never duplicate an invoice.
/// </summary>
public class Invoice : Entity
{
    /// <summary>Unique human-readable invoice number (for example "INV-2026-000001").</summary>
    [Required]
    [MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid OrderId { get; set; }

    public virtual Order? Order { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime IssueDateUtc { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ProductDiscount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CouponDiscount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShippingCharge { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Tax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal GrandTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OutstandingAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundedAmount { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;

    [Column(TypeName = "datetime2")]
    public DateTime GeneratedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? SentAtUtc { get; set; }

    /// <summary>Document revision. Bumped every regeneration; the number never changes.</summary>
    public int Version { get; set; } = 1;

    public virtual ICollection<InvoiceSendLog> SendLogs { get; set; } = new List<InvoiceSendLog>();
}
