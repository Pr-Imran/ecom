using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A saved delivery or billing address belonging to a customer. A customer can
/// have several addresses, but at most one default shipping and one default
/// billing address. Every read and mutation is scoped to the owning customer id;
/// one customer can never access another customer's addresses.
/// </summary>
public class CustomerAddress : Entity
{
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>A short label such as "Home" or "Office".</summary>
    [Required]
    [MaxLength(50)]
    public string Label { get; set; } = "Home";

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

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    [Required]
    [MaxLength(2)]
    public string CountryCode { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? DeliveryInstructions { get; set; }

    public bool IsDefaultShipping { get; set; }

    public bool IsDefaultBilling { get; set; }

    /// <summary>Optional future geolocation field; not used for order fulfilment yet.</summary>
    [Column(TypeName = "decimal(10,7)")]
    public decimal? Latitude { get; set; }

    /// <summary>Optional future geolocation field; not used for order fulfilment yet.</summary>
    [Column(TypeName = "decimal(10,7)")]
    public decimal? Longitude { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Produces an immutable snapshot of this address. Orders must persist a
    /// snapshot rather than a reference so that later edits to the address book
    /// never change the values recorded on an already-placed order.
    /// </summary>
    public AddressSnapshot CreateSnapshot() => new(
        RecipientName,
        Phone,
        AddressLine1,
        AddressLine2,
        Area,
        City,
        Region,
        PostalCode,
        CountryCode,
        DeliveryInstructions);
}

/// <summary>
/// Immutable value object capturing an address at a point in time. Orders store
/// a snapshot at creation; editing the source address afterwards never mutates
/// the snapshot, preserving order history independence.
/// </summary>
public sealed record AddressSnapshot(
    string RecipientName,
    string? Phone,
    string AddressLine1,
    string? AddressLine2,
    string? Area,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? DeliveryInstructions);
