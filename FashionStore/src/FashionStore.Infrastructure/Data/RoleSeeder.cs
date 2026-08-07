using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static FashionStore.Infrastructure.Data.ApplicationPermissions;

namespace FashionStore.Infrastructure.Data;

public interface IRoleSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public class RoleSeeder : IRoleSeeder
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _context;
    private readonly ILogger<RoleSeeder> _logger;

    public RoleSeeder(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        ILogger<RoleSeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var roles = GetDefaultRoles();

        foreach (var role in roles)
        {
            if (await _roleManager.FindByNameAsync(role.Name!) == null)
            {
                var result = await _roleManager.CreateAsync(role);
                if (result.Succeeded)
                {
                    await AssignPermissionsToRole(role.Name!, role);
                    _logger.LogInformation("Created role: {RoleName}", role.Name);
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        _logger.LogError("Error creating role {RoleName}: {Error}", role.Name, error.Description);
                    }
                }
            }
        }

        var superAdmin = await _roleManager.FindByNameAsync("SuperAdmin");
        if (superAdmin != null && !(await _context.UserRoles.AnyAsync(x => x.RoleId == superAdmin.Id, cancellationToken)))
        {
            _logger.LogWarning("No SuperAdmin user found. Run the seed administrator endpoint in development.");
        }
    }

    private List<ApplicationRole> GetDefaultRoles()
    {
        var now = DateTime.UtcNow;

        return new List<ApplicationRole>
        {
            new ApplicationRole
            {
                Name = "SuperAdmin",
                NormalizedName = "SUPERADMIN",
                Description = "Full system access",
                IsSystemRole = true,
                CreatedAtUtc = now
            },
            new ApplicationRole
            {
                Name = "Admin",
                NormalizedName = "ADMIN",
                Description = "Administrative access with limited system settings",
                IsSystemRole = true,
                CreatedAtUtc = now
            },
            new ApplicationRole
            {
                Name = "ProductManager",
                NormalizedName = "PRODUCTMANAGER",
                Description = "Manage products, categories, brands, and inventory",
                IsSystemRole = true,
                CreatedAtUtc = now
            },
            new ApplicationRole
            {
                Name = "OrderManager",
                NormalizedName = "ORDERMANAGER",
                Description = "Manage orders, invoices, and customer support",
                IsSystemRole = true,
                CreatedAtUtc = now
            },
            new ApplicationRole
            {
                Name = "InventoryManager",
                NormalizedName = "INVENTORYMANAGER",
                Description = "Manage product inventory and stock levels",
                IsSystemRole = true,
                CreatedAtUtc = now
            },
            new ApplicationRole
            {
                Name = "CustomerSupport",
                NormalizedName = "CUSTOMERSUPPORT",
                Description = "Handle customer inquiries and order issues",
                IsSystemRole = true,
                CreatedAtUtc = now
            },
            new ApplicationRole
            {
                Name = "ContentManager",
                NormalizedName = "CONTENTMANAGER",
                Description = "Manage website content, reviews, and media",
                IsSystemRole = true,
                CreatedAtUtc = now
            },
            new ApplicationRole
            {
                Name = "Customer",
                NormalizedName = "CUSTOMER",
                Description = "Standard customer access",
                IsSystemRole = true,
                CreatedAtUtc = now
            }
        };
    }

    private async Task AssignPermissionsToRole(string roleName, ApplicationRole role)
    {
        var permissions = GetPermissionsForRole(roleName);
        var existingClaims = await _roleManager.GetClaimsAsync(role);
        var existingPermissionValues = existingClaims.Select(c => c.Value).ToHashSet();

        foreach (var permission in permissions)
        {
            if (!existingPermissionValues.Contains(permission))
            {
                await _roleManager.AddClaimAsync(role, new System.Security.Claims.Claim("permission", permission));
            }
        }

        _logger.LogInformation("Assigned {Count} permissions to role {RoleName}", permissions.Length, roleName);
    }

    private string[] GetPermissionsForRole(string roleName)
    {
        return roleName switch
        {
            "SuperAdmin" => ApplicationPermissions.AllPermissions,
            "Admin" => new[]
            {
                Dashboard.View,
                Products.View, Products.Create, Products.Update, Products.Delete, Products.ManageInventory,
                Categories.Manage, Brands.Manage,
                Orders.View, Orders.UpdateStatus, Orders.Cancel, Orders.Refund, Orders.PrintInvoice,
                Customers.View, Customers.Update,
                Reviews.Manage, Promotions.Manage, Coupons.Manage, Content.Manage,
                Reports.View,
                Users.Manage,
                AuditLogs.View
            },
            "ProductManager" => new[]
            {
                Dashboard.View,
                Products.View, Products.Create, Products.Update, Products.Delete, Products.ManageInventory,
                Categories.Manage, Brands.Manage,
                Reports.View
            },
            "OrderManager" => new[]
            {
                Dashboard.View,
                Orders.View, Orders.UpdateStatus, Orders.Cancel, Orders.Refund, Orders.PrintInvoice,
                Customers.View, Customers.Update,
                Reviews.Manage,
                Reports.View
            },
            "InventoryManager" => new[]
            {
                Dashboard.View,
                Products.View, Products.ManageInventory,
                Reports.View
            },
            "CustomerSupport" => new[]
            {
                Dashboard.View,
                Orders.View,
                Customers.View,
                Products.View,
                Reports.View
            },
            "ContentManager" => new[]
            {
                Dashboard.View,
                Content.Manage,
                Reviews.Manage,
                Brands.Manage,
                Reports.View
            },
            "Customer" => new[]
            {
                Products.View,
                Orders.View,
                Customers.View
            },
            _ => Array.Empty<string>()
        };
    }
}
