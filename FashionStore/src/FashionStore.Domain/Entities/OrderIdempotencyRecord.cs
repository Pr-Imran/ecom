using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionStore.Domain.Entities;

/// <summary>
/// Guards against duplicate order creation from double clicks, mobile retries,
/// browser refreshes, slow connections and repeated API requests. One record is
/// inserted per unique idempotency key; the key is unique so a second attempt with
/// the same key returns the already-created order instead of placing a new one.
/// </summary>
public class OrderIdempotencyRecord : Entity
{
    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public Guid OrderId { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Used to purge stale records; not enforced as a hard lifetime.</summary>
    [Column(TypeName = "datetime2")]
    public DateTime? ExpiresAtUtc { get; set; }
}
