using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A counter used to generate sequential, human-readable public order numbers.
/// Each row tracks the last number issued for a given prefix within a year, so the
/// generator can produce monotonically increasing numbers without scanning orders.
/// The (Prefix, Year) combination is unique.
/// </summary>
public class OrderNumberSequence : Entity
{
    [Required]
    [MaxLength(20)]
    public string Prefix { get; set; } = string.Empty;

    public int Year { get; set; }

    public long LastNumber { get; set; }
}
