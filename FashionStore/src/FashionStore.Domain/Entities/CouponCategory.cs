namespace FashionStore.Domain.Entities;

/// <summary>
/// Join table linking a coupon to the categories its discount applies to. When a
/// coupon has category restrictions, only items in at least one of these categories
/// are eligible; if the list is empty the coupon is not category restricted.
/// </summary>
public class CouponCategory : Entity
{
    public Guid CouponId { get; set; }
    public Guid CategoryId { get; set; }
    public virtual Coupon? Coupon { get; set; }
    public virtual Category? Category { get; set; }
}
