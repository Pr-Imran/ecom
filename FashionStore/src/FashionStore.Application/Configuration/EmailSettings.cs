namespace FashionStore.Application.Configuration;

public sealed class EmailSettings
{
    public const string SectionName = "Email";
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    
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
    
    // Legacy single SMTP for backward compatibility
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public string SmtpUsername { get; init; } = string.Empty;
    public string SmtpPassword { get; init; } = string.Empty;
    public bool UseSsl { get; init; } = true;
}
