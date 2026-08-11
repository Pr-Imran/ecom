using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FashionStore.Domain.Enums;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single recorded transition in a return's lifecycle. Every status change is
/// captured so the history is auditable end to end; the first entry records the
/// initial request submission.
/// </summary>
public class ReturnStatusHistory : Entity
{
    public Guid ReturnRequestId { get; set; }
    public virtual ReturnRequest? ReturnRequest { get; set; }

    public ReturnStatus? FromStatus { get; set; }

    public ReturnStatus ToStatus { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
