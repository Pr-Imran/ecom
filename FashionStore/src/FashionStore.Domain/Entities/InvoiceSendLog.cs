using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// One recorded attempt to email an invoice PDF to the customer. Kept so
/// administrators can see the send history for an invoice: who it went to, when it
/// was sent, whether the delivery succeeded and any error surfaced by the SMTP
/// provider.
/// </summary>
public class InvoiceSendLog : Entity
{
    public Guid InvoiceId { get; set; }

    public virtual Invoice? Invoice { get; set; }

    [Required]
    [MaxLength(254)]
    public string SentTo { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? SentBy { get; set; }

    public bool Succeeded { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime SentAtUtc { get; set; }
}
