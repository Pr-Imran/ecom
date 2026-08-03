using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

public class WarehouseStock : AuditedEntity
{
    public Guid WarehouseId { get; set; }
    public virtual Warehouse? Warehouse { get; set; }

    public Guid ProductVariantId { get; set; }
    public virtual ProductVariant? Variant { get; set; }

    public int OnHandQuantity { get; set; }

    public int ReservedQuantity { get; set; }

    public int AvailableQuantity => OnHandQuantity - ReservedQuantity;

    public int? LowStockThreshold { get; set; }

    public int? ReorderLevel { get; set; }

    public bool AllowBackorder { get; set; }

    [Timestamp]
    public uint[] RowVersion { get; set; } = Array.Empty<uint>();
}
