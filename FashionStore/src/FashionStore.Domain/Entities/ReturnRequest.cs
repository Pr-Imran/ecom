using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A customer return request against an order. The financial reference data comes
/// from the order's immutable snapshots; returned quantities are capped at what was
/// purchased and duplicate completed returns are refused. Every status change is
/// recorded in <see cref="ReturnStatusHistory"/> so the whole workflow (request →
/// approval → shipping → receipt → inspection → refund/exchange → close) is auditable
/// end to end.
/// </summary>
public class ReturnRequest : AuditedEntity
{
    /// <summary>Human-readable public return number shown to the customer (for example "RMA-...").</summary>
    [Required]
    [MaxLength(50)]
    public string ReturnNumber { get; set; } = string.Empty;

    public Guid OrderId { get; set; }
    public virtual Order? Order { get; set; }

    /// <summary>Id of the signed-in customer who placed the order, or null for guest orders.</summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>Email captured at checkout, used to verify guest return access.</summary>
    [MaxLength(254)]
    public string? GuestEmail { get; set; }

    /// <summary>Customer name snapshot taken from the order at request time.</summary>
    [MaxLength(200)]
    public string? CustomerName { get; set; }

    [MaxLength(30)]
    public string? GuestPhone { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;

    public ReturnReasonCode ReasonCode { get; set; }

    [MaxLength(2000)]
    public string? CustomerNotes { get; set; }

    /// <summary>True when the customer asked for an exchange instead of a refund.</summary>
    public bool IsExchange { get; set; }

    /// <summary>Total amount that can be refunded for this return, computed at request time.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundableAmount { get; set; }

    /// <summary>Total amount actually refunded against this return.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundedAmount { get; set; }

    /// <summary>Courier tracking number captured when the customer ships the items back.</summary>
    [MaxLength(100)]
    public string? TrackingNumber { get; set; }

    /// <summary>Courier code for the return tracking number (for example "ups", "fedex").</summary>
    [MaxLength(50)]
    public string? CarrierCode { get; set; }

    /// <summary>Internal notes recorded by the support team.</summary>
    [MaxLength(1000)]
    public string? AdminNotes { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? ApprovedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? RejectedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? ReceivedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? InspectedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? RefundedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Stable rejection reason code when the return was rejected.</summary>
    [MaxLength(50)]
    public string? RejectionReasonCode { get; set; }

    /// <summary>Free-form note explaining why the return was rejected.</summary>
    [MaxLength(1000)]
    public string? RejectionNote { get; set; }

    /// <summary>Decision recorded at inspection (refund or exchange).</summary>
    public ReturnResolution Resolution { get; set; } = ReturnResolution.None;

    /// <summary>
    /// True when the customer withdrew the request before it progressed. Withdrawn
    /// requests release their claimed quantity so the customer can submit a new one.
    /// </summary>
    public bool IsWithdrawn { get; set; }

    public virtual ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();
    public virtual ICollection<ReturnStatusHistory> StatusHistory { get; set; } = new List<ReturnStatusHistory>();
    public virtual ICollection<ReturnAttachment> Attachments { get; set; } = new List<ReturnAttachment>();
    public virtual ICollection<ExchangeRequest> ExchangeRequests { get; set; } = new List<ExchangeRequest>();
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
