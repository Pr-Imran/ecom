using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;

namespace FashionStore.Web.Infrastructure;

/// <summary>
/// Renders a maintenance page when the store is in maintenance mode. Authenticated
/// administrators bypass the gate so they can preview the storefront while the
/// public sees the maintenance screen. The setting is read from the (cached)
/// website settings service so toggling maintenance mode takes effect immediately.
/// </summary>
public sealed class MaintenanceModeMiddleware
{
    private readonly RequestDelegate _next;

    public MaintenanceModeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IWebsiteSettingsService settings,
        ILogger<MaintenanceModeMiddleware> logger)
    {
        // Allow static assets and the maintenance endpoint through unconditionally.
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var isAdmin = context.User?.Identity?.IsAuthenticated == true &&
                      (context.User.IsInRole("SuperAdmin") || context.User.IsInRole("Admin"));

        // Admins always get through so they can review the storefront during maintenance.
        if (isAdmin || context.Request.Path.StartsWithSegments("/admin") || context.Request.Path.StartsWithSegments("/hangfire"))
        {
            await _next(context);
            return;
        }

        try
        {
            var snapshot = await settings.GetSettingsAsync(context.RequestAborted);
            if (snapshot.Maintenance.MaintenanceMode)
            {
                await RenderMaintenancePageAsync(context, snapshot.Maintenance.MaintenanceMessage);
                return;
            }
        }
        catch (Exception ex)
        {
            // If settings cannot be read, fail open rather than blacking out the store.
            logger.LogWarning(ex, "Maintenance mode check failed; serving storefront normally.");
        }

        await _next(context);
    }

    private static async Task RenderMaintenancePageAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.RetryAfter = "3600";

        var safeMessage = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(message) ? "We'll be back soon." : message);
        await context.Response.WriteAsync(
            "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />" +
            "<title>Under Maintenance</title></head>" +
            "<body style=\"font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f9fafb;margin:0;display:flex;align-items:center;justify-content:center;min-height:100vh;\">" +
            "<div style=\"text-align:center;padding:2rem;max-width:480px;\">" +
            "<div style=\"font-size:3rem;margin-bottom:1rem;\">&#128736;</div>" +
            "<h1 style=\"font-size:1.5rem;color:#111827;margin:0 0 0.5rem;\">Under Maintenance</h1>" +
            $"<p style=\"color:#6b7280;\">{safeMessage}</p>" +
            "</div></body></html>");
    }
}
