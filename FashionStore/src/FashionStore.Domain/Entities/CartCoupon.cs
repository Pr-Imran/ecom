using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// The coupon currently applied to a customer's cart. One row per customer; the
/// cart discount is always recomputed server-side from this reference, so a coupon
/// that later becomes ineligible is silently dropped rather than over-credited.
/// </summary>
public class CartCoupon : Entity
{
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public Guid CouponId { get; set; }
    public virtual Coupon? Coupon { get; set; }

    public DateTime AppliedAtUtc { get; set; }
}
