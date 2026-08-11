using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A configurable reason card shown to the customer when starting a return. The
/// catalogue is seeded with the built-in <see cref="Enums.ReturnReasonCode"/> values;
/// rows can be re-labelled, disabled or re-ordered by administrators.
/// </summary>
public class ReturnReason : AuditedEntity
{
    /// <summary>Stable code matching <see cref="Enums.ReturnReasonCode"/>.</summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Whether a photo is required when this reason is selected.</summary>
    public bool RequiresPhoto { get; set; }

    /// <summary>Whether selecting this reason allows the order's shipping charge to be refunded.</summary>
    public bool AllowShippingRefund { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
