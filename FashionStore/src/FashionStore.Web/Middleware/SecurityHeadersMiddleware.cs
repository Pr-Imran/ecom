namespace FashionStore.Web.Middleware;

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}

/// <summary>
/// Adds defensive HTTP headers to every response. The Content-Security-Policy is
/// kept permissive enough for the existing server-rendered views (inline scripts
/// and inline styles are required by the Razor pages), while every other
/// directive is locked down: no plugins, same-origin frames only, self-hosted
/// resources only. Strict-Transport-Security is emitted only over HTTPS outside
/// Development, where it is also added by <c>UseHsts()</c>.
/// </summary>
public class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "frame-src 'self';";

    private const string StrictTransportSecurity = "max-age=31536000; includeSubDomains; preload";

    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=(), battery=(), accelerometer=(), gyroscope=()";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;

        if (!_environment.IsDevelopment() &&
            context.Request.IsHttps &&
            !headers.ContainsKey("Strict-Transport-Security"))
        {
            headers["Strict-Transport-Security"] = StrictTransportSecurity;
        }

        await _next(context);
    }
}
