using System.ComponentModel.DataAnnotations;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// An immutable address snapshot stored on an order. The customer's editable
/// address book entry is intentionally not referenced here; the full address is
/// copied at placement time so the order remains accurate if the saved address is
/// later edited or deleted. The address is attached to its order through
/// <see cref="Order.ShippingAddressId"/> / <see cref="Order.BillingAddressId"/>;
/// <see cref="AddressType"/> records which role it played at placement time.
/// </summary>
public class OrderAddress : Entity
{
    public OrderAddressType AddressType { get; set; }

    [Required]
    [MaxLength(200)]
    public string RecipientName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [Required]
    [MaxLength(200)]
    public string AddressLine1 { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? AddressLine2 { get; set; }

    [MaxLength(100)]
    public string? Area { get; set; }

    [Required]
    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Region { get; set; }

    [Required]
    [MaxLength(20)]
    public string PostalCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(2)]
    public string CountryCode { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? DeliveryInstructions { get; set; }
}
