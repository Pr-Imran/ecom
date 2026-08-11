using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// Records a coupon redemption so administrators can audit usage history and the
/// engine can enforce total and per-customer usage limits. One row is created for
/// each checkout that uses a coupon; the amount recorded is the server-computed
/// discount actually applied.
/// </summary>
public class CouponUsage : Entity
{
    public Guid CouponId { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? OrderId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountDiscounted { get; set; }

    public DateTime UsedAtUtc { get; set; }

    /// <summary>
    /// When set, this redemption was voided (for example because the order it was
    /// recorded against was cancelled) and no longer counts towards usage limits.
    /// </summary>
    [Column(TypeName = "datetime2")]
    public DateTime? VoidedAtUtc { get; set; }

    public virtual Coupon? Coupon { get; set; }
}
