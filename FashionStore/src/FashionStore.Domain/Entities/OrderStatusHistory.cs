using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single recorded transition in an order's lifecycle. Every status change is
/// captured so the history is auditable end to end; the first entry records the
/// initial placement.
/// </summary>
public class OrderStatusHistory : Entity
{
    public Guid OrderId { get; set; }
    public virtual Order? Order { get; set; }

    public OrderStatus? FromStatus { get; set; }

    public OrderStatus ToStatus { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
