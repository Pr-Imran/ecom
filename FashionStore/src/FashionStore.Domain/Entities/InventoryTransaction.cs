using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

public class InventoryTransaction : Entity
{
    public Guid WarehouseId { get; set; }
    public virtual Warehouse? Warehouse { get; set; }

    public Guid ProductVariantId { get; set; }
    public virtual ProductVariant? Variant { get; set; }

    public int QuantityChange { get; set; }

    public int PreviousOnHand { get; set; }

    public int NewOnHand { get; set; }

    public int ReservedQuantityChange { get; set; }

    public int PreviousReserved { get; set; }

    public int NewReserved { get; set; }

    public StockAdjustmentReason Reason { get; set; }

    public InventoryReferenceType ReferenceType { get; set; } = InventoryReferenceType.None;

    [MaxLength(100)]
    public string? ReferenceId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(450)]
    public string? AdministratorId { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
