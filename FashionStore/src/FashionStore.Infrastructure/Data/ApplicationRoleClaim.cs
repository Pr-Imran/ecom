using Microsoft.AspNetCore.Identity;

namespace FashionStore.Infrastructure.Data;

public class ApplicationRoleClaim : IdentityRoleClaim<string>
{
    public virtual ApplicationRole Role { get; set; } = null!;
}
