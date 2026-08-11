using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A replacement item arranged when a return is resolved as an exchange. The
/// replacement variant and price are snapshotted at decision time so the record
/// stays readable if the catalogue changes afterwards.
/// </summary>
public class ExchangeRequest : AuditedEntity
{
    public Guid ReturnRequestId { get; set; }
    public virtual ReturnRequest? ReturnRequest { get; set; }

    public Guid OrderId { get; set; }

    /// <summary>The variant chosen as the replacement.</summary>
    public Guid ProductVariantId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    public ExchangeStatus Status { get; set; } = ExchangeStatus.Pending;

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(450)]
    public string? CompletedBy { get; set; }
}
