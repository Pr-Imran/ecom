namespace FashionStore.Domain.Entities;

/// <summary>
/// Join table linking a coupon to specific products the discount applies to. When a
/// coupon has product restrictions, only these products are eligible; an empty list
/// means the coupon is not product restricted.
/// </summary>
public class CouponProduct : Entity
{
    public Guid CouponId { get; set; }
    public Guid ProductId { get; set; }
    public virtual Coupon? Coupon { get; set; }
    public virtual Product? Product { get; set; }
}
