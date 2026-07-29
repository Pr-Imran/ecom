namespace FashionStore.Application.Configuration;

public sealed class EmailSettings
{
    public const string SectionName = "Email";
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public string SmtpUsername { get; init; } = string.Empty;
    public string SmtpPassword { get; init; } = string.Empty;
    public bool UseSsl { get; init; } = true;
}
