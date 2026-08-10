using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A free-form note attached to an order. Notes are internal (staff-facing) and do
/// not replace the status history; they are kept separate so the two can evolve
/// independently.
/// </summary>
public class OrderNote : Entity
{
    public Guid OrderId { get; set; }
    public virtual Order? Order { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Note { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
