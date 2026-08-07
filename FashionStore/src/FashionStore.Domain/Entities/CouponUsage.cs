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

    public virtual Coupon? Coupon { get; set; }
}
