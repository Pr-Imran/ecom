namespace FashionStore.Application.Configuration;

public sealed class SecuritySettings
{
    public const string SectionName = "Security";
    public bool RequireEmailConfirmation { get; init; } = true;
    public int MaxFailedLoginAttempts { get; init; } = 5;
    public int LockoutDurationMinutes { get; init; } = 15;
    public int PasswordRequiredLength { get; init; } = 8;
    public bool PasswordRequireDigit { get; init; } = true;
    public bool PasswordRequireUppercase { get; init; } = true;
    public bool PasswordRequireNonAlphanumeric { get; init; } = true;
    public int TokenExpiryHours { get; init; } = 24;
}
