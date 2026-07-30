using Microsoft.AspNetCore.Identity;

namespace FashionStore.Infrastructure.Data;

public class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public List<ApplicationRoleClaim> Claims { get; init; } = new();
    public List<ApplicationUserRole> UserRoles { get; init; } = new();
}
