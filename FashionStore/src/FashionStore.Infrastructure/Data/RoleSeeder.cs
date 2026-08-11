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

        await SeedReturnReasonCatalogueAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds the configurable return-reason catalogue with the built-in reason codes.
    /// Existing rows are updated in place so re-labelling and custom ordering by
    /// administrators is preserved across seeding runs.
    /// </summary>
    private async Task SeedReturnReasonCatalogueAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var catalogue = new[]
        {
            (Code: "ChangedMind", Label: "Changed my mind", Description: "The item no longer suits me.", RequiresPhoto: false, AllowShippingRefund: true),
            (Code: "WrongSize", Label: "Wrong size", Description: "It doesn't fit, please help me exchange or return it.", RequiresPhoto: false, AllowShippingRefund: true),
            (Code: "NotAsDescribed", Label: "Not as described", Description: "The item looks different from the listing.", RequiresPhoto: true, AllowShippingRefund: true),
            (Code: "Damaged", Label: "Damaged on arrival", Description: "The item arrived damaged or broken.", RequiresPhoto: true, AllowShippingRefund: true),
            (Code: "Defective", Label: "Defective or faulty", Description: "The item does not work as it should.", RequiresPhoto: true, AllowShippingRefund: true),
            (Code: "Unwanted", Label: "No longer wanted", Description: "I changed my mind about this purchase.", RequiresPhoto: false, AllowShippingRefund: false),
            (Code: "DuplicateOrder", Label: "Duplicate order", Description: "I accidentally ordered this more than once.", RequiresPhoto: false, AllowShippingRefund: true),
            (Code: "Other", Label: "Another reason", Description: "Something else went wrong.", RequiresPhoto: false, AllowShippingRefund: false)
        };

        var existing = await _context.ReturnReasons
            .AsNoTracking()
            .Select(r => r.Code)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, label, description, requiresPhoto, allowShippingRefund) in catalogue)
        {
            if (!existingSet.Contains(code))
            {
                _context.ReturnReasons.Add(new FashionStore.Domain.Entities.ReturnReason
                {
                    Code = code,
                    Label = label,
                    Description = description,
                    RequiresPhoto = requiresPhoto,
                    AllowShippingRefund = allowShippingRefund,
                    IsActive = true,
                    SortOrder = Array.FindIndex(catalogue, c => c.Code == code),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded return-reason catalogue with {Count} reasons", catalogue.Length);
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
                Orders.View, Orders.UpdateStatus, Orders.Cancel, Orders.Refund, Orders.PrintInvoice, Orders.AddNote,
                Returns.View, Returns.Review, Returns.Inspect, Returns.Restock, Returns.Refund, Returns.Exchange, Returns.Complete,
                Customers.View, Customers.Update,
                Reviews.Manage, Promotions.Manage, Coupons.Manage, Shipping.Manage, Content.Manage,
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
                Orders.View, Orders.UpdateStatus, Orders.Cancel, Orders.Refund, Orders.PrintInvoice, Orders.AddNote,
                Returns.View, Returns.Review, Returns.Inspect, Returns.Restock, Returns.Refund, Returns.Exchange, Returns.Complete,
                Customers.View, Customers.Update,
                Reviews.Manage,
                Reports.View
            },
            "InventoryManager" => new[]
            {
                Dashboard.View,
                Products.View, Products.ManageInventory,
                Returns.View, Returns.Restock,
                Reports.View
            },
            "CustomerSupport" => new[]
            {
                Dashboard.View,
                Orders.View, Orders.AddNote,
                Returns.View, Returns.Review, Returns.Complete,
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
