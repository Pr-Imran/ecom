namespace FashionStore.Domain.Entities;

/// <summary>
/// Join table listing products excluded from a coupon. Exclusions take precedence
/// over any other restriction, so a coupon can never discount an excluded product.
/// </summary>
public class CouponExcludedProduct : Entity
{
    public Guid CouponId { get; set; }
    public Guid ProductId { get; set; }
    public virtual Coupon? Coupon { get; set; }
    public virtual Product? Product { get; set; }
}
