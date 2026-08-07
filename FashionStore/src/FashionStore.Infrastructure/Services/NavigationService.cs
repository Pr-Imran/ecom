using FashionStore.Application.DTOs.Navigation;
using FashionStore.Application.Interfaces;
using FashionStore.Application.Services;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Globalization;

namespace FashionStore.Infrastructure.Services;

public class NavigationService : INavigationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ICartService _cartService;
    private readonly ILogger<NavigationService> _logger;

    public NavigationService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ICartService cartService,
        ILogger<NavigationService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _cartService = cartService;
        _logger = logger;
    }

    public Task<IEnumerable<NavigationItem>> GetPublicNavigationAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<NavigationItem>
        {
            new NavigationItem("home", "Home", "/", "home"),
            new NavigationItem("products", "Products", "/products", "shopping-bag"),
            new NavigationItem("categories", "Categories", "/categories", "grid"),
            new NavigationItem("brands", "Brands", "/brands", "tag"),
            new NavigationItem("about", "About Us", "/about", "info"),
            new NavigationItem("contact", "Contact", "/contact", "mail")
        };

        return Task.FromResult(items.AsEnumerable());
    }

    public Task<IEnumerable<NavigationItem>> GetMobileNavigationAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        var items = new List<NavigationItem>
        {
            new NavigationItem("home", "Home", "/", "home"),
            new NavigationItem("categories", "Categories", "/categories", "grid"),
            new NavigationItem("search", "Search", "/search", "search"),
            new NavigationItem("wishlist", "Wishlist", "/wishlist", "heart"),
            new NavigationItem("account", "Account", userId != null ? "/account" : "/account/login", "user")
        };

        return Task.FromResult(items.AsEnumerable());
    }

    public async Task<IEnumerable<NavigationItem>> GetAdminNavigationAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await GetAdminUserAsync(userId, cancellationToken);
        var permissions = await GetUserPermissionsAsync(userId, cancellationToken);

        var items = new List<NavigationItem>();

        if (HasAnyPermission(permissions, new[] { "Dashboard.View" }))
        {
            items.Add(new NavigationItem("dashboard", "Dashboard", "/admin", "layout-dashboard"));
        }

        if (HasAnyPermission(permissions, new[] { "Products.View" }))
        {
            items.Add(new NavigationItem("products", "Products", "/admin/products", "package")
            {
                Children = new[]
                {
                    new NavigationItem("products-list", "All Products", "/admin/products", null),
                    HasPermission(permissions, "Products.Create") ? new NavigationItem("products-create", "Add New", "/admin/products/create", null) : null,
                    HasPermission(permissions, "Categories.Manage") ? new NavigationItem("categories", "Categories", "/admin/categories", null) : null,
                    HasPermission(permissions, "Brands.Manage") ? new NavigationItem("brands", "Brands", "/admin/brands", null) : null,
                    HasPermission(permissions, "Products.View") ? new NavigationItem("variations", "Variations", "/admin/variations", null) : null,
                    HasPermission(permissions, "Products.View") ? new NavigationItem("attributes", "Attributes", "/admin/attributes", null) : null,
                    HasPermission(permissions, "Products.View") ? new NavigationItem("images", "Images", "/admin/images", null) : null,
                    HasPermission(permissions, "Products.ManageInventory") ? new NavigationItem("inventory", "Inventory", "/admin/inventory", null) : null
                }.Where(c => c != null).Cast<NavigationItem>()
            });
        }

        if (HasAnyPermission(permissions, new[] { "Orders.View" }))
        {
            items.Add(new NavigationItem("orders", "Orders", "/admin/orders", "shopping-cart"));
        }

        if (HasAnyPermission(permissions, new[] { "Customers.View" }))
        {
            items.Add(new NavigationItem("customers", "Customers", "/admin/customers", "users"));
        }

        if (HasAnyPermission(permissions, new[] { "Coupons.Manage", "Promotions.Manage" }))
        {
            items.Add(new NavigationItem("promotions", "Coupons & Promotions", "/admin/promotions", "tag")
            {
                Children = new[]
                {
                    HasPermission(permissions, "Coupons.Manage") ? new NavigationItem("coupons", "Coupons", "/admin/coupons", null) : null,
                    HasPermission(permissions, "Promotions.Manage") ? new NavigationItem("promotions", "Promotions", "/admin/promotions", null) : null
                }.Where(c => c != null).Cast<NavigationItem>()
            });
        }

        if (HasAnyPermission(permissions, new[] { "Reviews.Manage" }))
        {
            items.Add(new NavigationItem("reviews", "Reviews", "/admin/reviews", "message-square"));
        }

        if (HasAnyPermission(permissions, new[] { "Reports.View" }))
        {
            items.Add(new NavigationItem("reports", "Reports", "/admin/reports", "bar-chart-3"));
        }

        if (HasAnyPermission(permissions, new[] { "Users.Manage", "Roles.Manage" }))
        {
            items.Add(new NavigationItem("users", "User Management", "/admin/users", "shield")
            {
                Children = new[]
                {
                    HasPermission(permissions, "Users.Manage") ? new NavigationItem("users-list", "Users", "/admin/users", null) : null,
                    HasPermission(permissions, "Roles.Manage") ? new NavigationItem("roles", "Roles", "/admin/roles", null) : null
                }.Where(c => c != null).Cast<NavigationItem>()
            });
        }

        if (HasAnyPermission(permissions, new[] { "AuditLogs.View" }))
        {
            items.Add(new NavigationItem("audit", "Audit Logs", "/admin/audit", "file-text"));
        }

        items.Add(new NavigationItem("settings", "Settings", "/admin/settings", "settings"));

        return items;
    }

    public Task<IEnumerable<NavigationItem>> GetAccountNavigationAsync(string userId, CancellationToken cancellationToken = default)
    {
        var items = new List<NavigationItem>
        {
            new NavigationItem("account-overview", "Overview", "/account", "user"),
            new NavigationItem("account-orders", "My Orders", "/account/orders", "shopping-bag"),
            new NavigationItem("account-wishlist", "Wishlist", "/wishlist", "heart"),
            new NavigationItem("account-addresses", "Addresses", "/account/addresses", "map-pin"),
            new NavigationItem("account-reviews", "My Reviews", "/account/reviews", "message-square"),
            new NavigationItem("account-security", "Security", "/account/security", "lock"),
            new NavigationItem("account-settings", "Settings", "/account/settings", "settings")
        };

        return Task.FromResult(items.AsEnumerable());
    }

    public IEnumerable<BreadcrumbItem> GenerateBreadcrumbs(IEnumerable<(string Label, string? Url)> segments, string? currentPage = null)
    {
        var segmentsList = segments.ToList();
        var breadcrumbs = new List<BreadcrumbItem>();

        breadcrumbs.Add(new BreadcrumbItem("Home", "/", false));

        for (int i = 0; i < segmentsList.Count; i++)
        {
            var segment = segmentsList[i];
            var isLast = i == segmentsList.Count - 1;
            breadcrumbs.Add(new BreadcrumbItem(
                segment.Label,
                segment.Url,
                isLast && currentPage != segment.Label
            ));
        }

        if (!string.IsNullOrEmpty(currentPage))
        {
            breadcrumbs.Add(new BreadcrumbItem(currentPage, null, true));
        }

        return breadcrumbs;
    }

    public async Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);

        return new UserProfile(
            user.Id,
            user.Email!,
            user.DisplayName ?? user.FullName,
            user.ProfileImageUrl,
            roles.ToArray()
        );
    }

    public async Task<CartSummary> GetCartSummaryAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new CartSummary(0, 0m, "$0.00");
        }

        try
        {
            var cart = await _cartService.GetCartAsync(userId, cancellationToken);
            return new CartSummary(
                cart.ItemCount,
                cart.Subtotal,
                cart.FormattedSubtotal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cart summary for user {UserId}", userId);
            return new CartSummary(0, 0m, "$0.00");
        }
    }

    public IEnumerable<Announcement> GetActiveAnnouncements()
    {
        var announcements = new List<Announcement>
        {
            new Announcement(
                "promo-1",
                "🎉 Free shipping on orders over $100! Use code: FREESHIP",
                "/products",
                "Shop Now",
                DateTime.UtcNow.AddDays(7),
                "primary"
            ),
            new Announcement(
                "sale-1",
                "⚡ Flash Sale: Up to 50% off selected items. Ends tonight!",
                "/products/sale",
                "View Sale",
                DateTime.UtcNow.AddDays(1),
                "danger"
            )
        };

        var now = DateTime.UtcNow;
        return announcements.Where(a => a.ExpiresAt == null || a.ExpiresAt > now);
    }

    private async Task<ApplicationUser?> GetAdminUserAsync(string userId, CancellationToken cancellationToken)
    {
        return await _userManager.FindByIdAsync(userId);
    }

    private async Task<string[]> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return Array.Empty<string>();

        var claims = await _userManager.GetClaimsAsync(user);
        var roles = await _userManager.GetRolesAsync(user);

        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var roleClaims = await _roleManager.GetClaimsAsync(role);
                foreach (var claim in roleClaims)
                {
                    claims.Add(claim);
                }
            }
        }

        return claims.Where(c => c.Type == "permission").Select(c => c.Value).Distinct().ToArray();
    }

    private bool HasAnyPermission(string[] permissions, string[] required)
    {
        return permissions.Intersect(required, StringComparer.OrdinalIgnoreCase).Any()
            || permissions.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase);
    }

    private bool HasPermission(string[] permissions, string permission)
    {
        return permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)
            || permissions.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase);
    }
}
