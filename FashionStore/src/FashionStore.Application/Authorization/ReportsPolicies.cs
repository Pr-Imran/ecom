namespace FashionStore.Application.Authorization;

/// <summary>
/// Policies guarding the administration reporting surface. Generating,
/// filtering and exporting reports all map to the single <c>Reports.View</c>
/// capability.
/// </summary>
public static class ReportsPolicies
{
    public const string ReportsView = "Reports.View";
}
