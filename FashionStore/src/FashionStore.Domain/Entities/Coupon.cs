using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A discount coupon that customers can apply to a cart. Codes are unique and
/// case-insensitive; every validation (dates, minimum order, usage limits,
/// restrictions) is performed server-side at calculation time so a client can
/// never bypass a rule.
/// </summary>
public class Coupon : Entity
{
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string NormalizedCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public DiscountType DiscountType { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MaxDiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MinOrderValue { get; set; }

    public bool IsFreeShipping { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsAutoApply { get; set; }

    public bool IsFirstOrderOnly { get; set; }

    public int? TotalUsageLimit { get; set; }

    public int PerCustomerLimit { get; set; } = 1;

    public DateTime? StartAtUtc { get; set; }

    public DateTime? EndAtUtc { get; set; }

    [MaxLength(450)]
    public string? CustomerId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public virtual ICollection<CouponCategory> CouponCategories { get; set; } = new List<CouponCategory>();
    public virtual ICollection<CouponBrand> CouponBrands { get; set; } = new List<CouponBrand>();
    public virtual ICollection<CouponProduct> CouponProducts { get; set; } = new List<CouponProduct>();
    public virtual ICollection<CouponExcludedProduct> CouponExcludedProducts { get; set; } = new List<CouponExcludedProduct>();
    public virtual ICollection<CouponUsage> CouponUsages { get; set; } = new List<CouponUsage>();
}
