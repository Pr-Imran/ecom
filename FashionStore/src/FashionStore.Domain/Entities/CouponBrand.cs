namespace FashionStore.Domain.Entities;

/// <summary>
/// Join table linking a coupon to the brands its discount applies to. When a coupon
/// has brand restrictions, only items from at least one of these brands are eligible;
/// an empty list means the coupon is not brand restricted.
/// </summary>
public class CouponBrand : Entity
{
    public Guid CouponId { get; set; }
    public Guid BrandId { get; set; }
    public virtual Coupon? Coupon { get; set; }
    public virtual Brand? Brand { get; set; }
}
