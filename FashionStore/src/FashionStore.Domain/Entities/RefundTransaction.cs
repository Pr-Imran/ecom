using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// An immutable audit record for a refund's lifecycle (created, succeeded, failed,
/// voided). Every action that moves money is logged here so the refund history is
/// verifiable without trusting the current refund state.
/// </summary>
public class RefundTransaction : Entity
{
    public Guid RefundId { get; set; }
    public virtual Refund? Refund { get; set; }

    /// <summary>Stable action key ("Created", "Succeeded", "Failed", "Voided").</summary>
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    [MaxLength(50)]
    public string? ResultCode { get; set; }

    [MaxLength(500)]
    public string? ResultMessage { get; set; }

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }
}
