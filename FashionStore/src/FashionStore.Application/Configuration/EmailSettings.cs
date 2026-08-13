namespace FashionStore.Application.Configuration;

/// <summary>
/// Email transport configuration. The <see cref="Provider"/> selects the active
/// provider: <c>Development</c> writes to the log, the well-known presets
/// (<c>Gmail</c>, <c>Outlook</c>, <c>Hotmail</c>, <c>Yahoo</c>) pre-fill the SMTP
/// server settings, <c>Custom</c> lets you bring your own domain mail server
/// (host, port, username, password, TLS) and <c>Api</c> reserves the seam for a
/// future HTTP-based API provider (SendGrid-style). Credentials must never be
/// logged; only the sanitized host/port are surfaced.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;

    /// <summary>Active provider: Development, Gmail, Outlook, Hotmail, Yahoo, Custom or Api.</summary>
    public string Provider { get; init; } = "Development";

    /// <summary>Public base URL used to build absolute links inside email templates.</summary>
    public string BaseUrl { get; init; } = "https://localhost:5001";

    /// <summary>Comma-separated administrator inboxes for alerts such as low-stock digests.</summary>
    public string AdminAlertRecipients { get; init; } = string.Empty;

    /// <summary>Delivery attempts before an email is marked failed.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Base (minutes) of the exponential backoff between retries.</summary>
    public int RetryBaseDelayMinutes { get; init; } = 5;

    // Custom / own-domain SMTP (also the destination of the well-known presets).
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public string SmtpUsername { get; init; } = string.Empty;
    public string SmtpPassword { get; init; } = string.Empty;
    public bool UseSsl { get; init; } = true;

    // Backwards-compatible blocks kept so existing configurations keep working.
    // Primary SMTP (Gmail)
    public string PrimarySmtpHost { get; init; } = string.Empty;
    public int PrimarySmtpPort { get; init; } = 587;
    public string PrimarySmtpUsername { get; init; } = string.Empty;
    public string PrimarySmtpPassword { get; init; } = string.Empty;
    public bool PrimaryUseSsl { get; init; } = true;

    // Fallback SMTP (cPanel/Domain)
    public string FallbackSmtpHost { get; init; } = string.Empty;
    public int FallbackSmtpPort { get; init; } = 465;
    public string FallbackSmtpUsername { get; init; } = string.Empty;
    public string FallbackSmtpPassword { get; init; } = string.Empty;
    public bool FallbackUseSsl { get; init; } = true;
}
