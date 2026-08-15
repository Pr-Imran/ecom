namespace FashionStore.Application.Configuration;

/// <summary>
/// Data Protection key-ring settings. Keys encrypt identity cookies, anti-forgery
/// tokens, password-reset / email-confirmation tokens and the guest access tokens.
/// The default (per-process, in-memory key ring) is acceptable for Development but
/// must be overridden in Production so a restart or a second instance does not
/// invalidate existing cookies and tokens.
/// </summary>
public sealed class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Absolute or content-root-relative directory where the key ring is persisted.
    /// Leave empty to use the built-in per-process key ring (Development only).
    /// </summary>
    public string? KeysDirectory { get; init; }

    /// <summary>
    /// Application name recorded in the key ring; must be identical across all
    /// instances sharing the key ring so protected payloads interoperate.
    /// </summary>
    public string ApplicationName { get; init; } = "FashionStore";
}
