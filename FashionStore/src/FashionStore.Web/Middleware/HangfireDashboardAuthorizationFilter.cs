using Hangfire.Dashboard;

namespace FashionStore.Web.Middleware;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User?.Identity?.IsAuthenticated == true
            && (httpContext.User.IsInRole("SuperAdmin") || httpContext.User.IsInRole("Admin"));
    }
}
