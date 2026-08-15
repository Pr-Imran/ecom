using Hangfire.Dashboard;

namespace FashionStore.Web.Middleware;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly string _requiredRole;

    public HangfireDashboardAuthorizationFilter(string requiredRole = "SuperAdmin")
    {
        _requiredRole = requiredRole;
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User?.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole(_requiredRole);
    }
}
