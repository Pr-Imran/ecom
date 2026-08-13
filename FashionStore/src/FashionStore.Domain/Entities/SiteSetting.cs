using System.ComponentModel.DataAnnotations;

namespace FashionStore.Domain.Entities;

/// <summary>
/// A single store-wide setting stored in the database. Settings are keyed by a
/// stable string and carry a JSON-encoded value plus metadata used by the admin
/// UI (description, group) and by the permissions layer (<see cref="IsProtected"/>).
///
/// Protected settings (for example currency, timezone or maintenance mode) can
/// only be changed by a SuperAdmin; the admin API rejects writes to protected
/// keys from lower-privileged users. Every change is written through the website
/// settings service, which audits the mutation and invalidates the settings
/// cache so storefront reads never see a stale value.
/// </summary>
public class SiteSetting : AuditedEntity
{
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON-encoded setting value.</summary>
    public string Value { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ValueType { get; set; } = "string";

    [MaxLength(200)]
    public string? Group { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>When true only a SuperAdmin may change this setting.</summary>
    public bool IsProtected { get; set; }
}
