using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A customer order placed at checkout. The financial fields are immutable
/// snapshots captured from the server-side checkout calculation at placement time
/// and must never be re-derived from live catalogue data afterwards. Product and
/// address details are stored on <see cref="OrderItem"/> and
/// <see cref="OrderAddress"/> so the order stays readable if the original product,
/// variant or address is later renamed, deactivated or removed.
/// </summary>
public class Order : Entity
{
    /// <summary>Human-readable public order number shown to the customer.</summary>
    [Required]
    [MaxLength(50)]
    public string PublicOrderNumber { get; set; } = string.Empty;

    /// <summary>Invoice number placeholder; assigned by the invoicing pipeline.</summary>
    [MaxLength(50)]
    public string? InvoiceNumber { get; set; }

    /// <summary>Id of the signed-in customer, or null for guest checkout.</summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>Email captured at checkout for guests (and mirrored for signed-in customers).</summary>
    [MaxLength(254)]
    public string? GuestEmail { get; set; }

    /// <summary>Phone captured at checkout for delivery contact.</summary>
    [MaxLength(30)]
    public string? GuestPhone { get; set; }

    /// <summary>Customer name snapshot taken from the shipping recipient at placement.</summary>
    [MaxLength(200)]
    public string? CustomerName { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    /// <summary>Promotional discount total (product / line promotions) applied at placement.</summary>
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
    public decimal RefundedAmount { get; set; }

    public OrderStatus OrderStatus { get; set; } = OrderStatus.Placed;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public FulfilmentStatus FulfilmentStatus { get; set; } = FulfilmentStatus.Unfulfilled;

    /// <summary>Stable payment method key (for example "cod" or "card").</summary>
    [MaxLength(50)]
    public string? PaymentMethodCode { get; set; }

    public Guid? ShippingMethodId { get; set; }

    [MaxLength(50)]
    public string? ShippingMethodCode { get; set; }

    [MaxLength(200)]
    public string? ShippingMethodName { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime UpdatedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? PaidAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? ShippedAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? DeliveredAtUtc { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>Stable cancellation reason code when the order was cancelled.</summary>
    [MaxLength(50)]
    public string? CancelledReasonCode { get; set; }

    /// <summary>Courier tracking number assigned when the order is shipped.</summary>
    [MaxLength(100)]
    public string? TrackingNumber { get; set; }

    /// <summary>Courier code for the tracking number (for example "ups", "fedex", "dhl").</summary>
    [MaxLength(50)]
    public string? CarrierCode { get; set; }

    /// <summary>Public tracking lookup url for the selected courier.</summary>
    [MaxLength(500)]
    public string? TrackingUrl { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? PackedAtUtc { get; set; }

    public Guid? ShippingAddressId { get; set; }

    public Guid? BillingAddressId { get; set; }

    public virtual ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    /// <summary>Immutable shipping address snapshot; one per order.</summary>
    public virtual OrderAddress? ShippingAddress { get; set; }

    /// <summary>Immutable billing address snapshot; one per order.</summary>
    public virtual OrderAddress? BillingAddress { get; set; }

    public virtual ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();

    public virtual ICollection<OrderNote> Notes { get; set; } = new List<OrderNote>();
}
