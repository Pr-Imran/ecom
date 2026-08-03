using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

public class StockReservation : AuditedEntity
{
    public Guid ProductVariantId { get; set; }
    public virtual ProductVariant? Variant { get; set; }

    public Guid? WarehouseId { get; set; }
    public virtual Warehouse? Warehouse { get; set; }

    public int Quantity { get; set; }

    [MaxLength(100)]
    public string CartReference { get; set; } = string.Empty;

    [Column(TypeName = "datetime2")]
    public DateTime ExpiresAtUtc { get; set; }

    public StockReservationStatus Status { get; set; } = StockReservationStatus.Active;

    [Column(TypeName = "datetime2")]
    public DateTime? ReleasedAtUtc { get; set; }

    [MaxLength(100)]
    public string? ReferenceId { get; set; }
}
