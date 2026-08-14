namespace FashionStore.Application.Authorization;

/// <summary>
/// Policies guarding the administration dashboard surface. Viewing the
/// dashboard maps to the single <c>Dashboard.View</c> capability.
/// </summary>
public static class DashboardPolicies
{
    public const string DashboardView = "Dashboard.View";
}
