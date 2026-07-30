using Microsoft.AspNetCore.Identity;

namespace FashionStore.Infrastructure.Data;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? ProfileImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedOutUntilUtc { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
        ? Email ?? UserName ?? "User"
        : $"{FirstName} {LastName}".Trim();

    public List<ApplicationUserRole> UserRoles { get; init; } = new();
    public List<AuditLog> AuditLogs { get; init; } = new();
}
