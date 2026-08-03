using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

public class Warehouse : AuditedEntity
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(150)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public int DisplayOrder { get; set; }

    public virtual ICollection<WarehouseStock> StockItems { get; set; } = new List<WarehouseStock>();
    public virtual ICollection<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
    public virtual ICollection<StockReservation> Reservations { get; set; } = new List<StockReservation>();
}
