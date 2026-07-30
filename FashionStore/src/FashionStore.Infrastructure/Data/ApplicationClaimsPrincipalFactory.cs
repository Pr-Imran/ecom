using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Data;

public class ApplicationClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public ApplicationClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim("userId", user.Id));
        identity.AddClaim(new Claim("email", user.Email ?? ""));
        identity.AddClaim(new Claim("displayName", user.DisplayName ?? user.FullName));
        identity.AddClaim(new Claim("isActive", user.IsActive.ToString().ToLower()));

        if (!string.IsNullOrEmpty(user.ProfileImageUrl))
        {
            identity.AddClaim(new Claim("profileImageUrl", user.ProfileImageUrl));
        }

        var userRoles = await UserManager.GetRolesAsync(user);
        foreach (var role in userRoles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var roleClaims = await GetRoleClaimsAsync(userRoles);
        foreach (var claim in roleClaims)
        {
            if (!identity.HasClaim(c => c.Type == claim.Type && c.Value == claim.Value))
            {
                identity.AddClaim(claim);
            }
        }

        return identity;
    }

    private async Task<List<Claim>> GetRoleClaimsAsync(IList<string> roles)
    {
        var claims = new List<Claim>();

        foreach (var roleName in roles)
        {
            var role = await RoleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var roleClaims = await RoleManager.GetClaimsAsync(role);
                claims.AddRange(roleClaims);
            }
        }

        return claims;
    }
}
