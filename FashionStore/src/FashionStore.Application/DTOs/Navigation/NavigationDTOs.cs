namespace FashionStore.Application.DTOs.Navigation;

public sealed record NavigationItem(
    string Id,
    string Label,
    string Url,
    string? Icon = null,
    IEnumerable<NavigationItem>? Children = null,
    string[]? RequiredPermissions = null,
    bool IsActive = false
);

public sealed record NavigationGroup(
    string Id,
    string Label,
    IEnumerable<NavigationItem> Items,
    bool IsExpanded = true
);

public sealed record BreadcrumbItem(
    string Label,
    string? Url = null,
    bool IsCurrent = false
);

public sealed record UserProfile(
    string UserId,
    string Email,
    string? DisplayName,
    string? ProfileImageUrl,
    string[] Roles
);

public sealed record CartSummary(
    int ItemCount,
    decimal TotalAmount,
    string FormattedTotal
);

public sealed record Announcement(
    string Id,
    string Message,
    string? LinkUrl = null,
    string? LinkText = null,
    DateTime? ExpiresAt = null,
    string Style = "default"
);
