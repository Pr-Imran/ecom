using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A counter used to generate sequential, unique invoice numbers. Each row tracks
/// the last number issued for a prefix within a year (year 0 disables year-aware
/// numbering). The (Prefix, Year) combination is unique; allocation increments the
/// counter inside the save that also persists the invoice, so a unique-index
/// conflict on <see cref="Invoice.InvoiceNumber"/> forces a retry that can never
/// duplicate a number.
/// </summary>
public class InvoiceNumberSequence : Entity
{
    [Required]
    [MaxLength(20)]
    public string Prefix { get; set; } = string.Empty;

    public int Year { get; set; }

    public long LastNumber { get; set; }
}
