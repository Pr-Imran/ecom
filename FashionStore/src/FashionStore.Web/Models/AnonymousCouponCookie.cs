namespace FashionStore.Web.Models;

/// <summary>
/// Cookie-backed coupon code for anonymous visitors. Only the raw code string is
/// stored; eligibility and pricing are always recomputed server-side against live
/// data when the cart is read, so a stale or revoked code is silently dropped.
/// </summary>
public static class AnonymousCouponCookie
{
    public const string CookieName = "fashionstore_coupon";
    private const int MaxLength = 50;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static string? Read(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var code) || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return code.Length > MaxLength ? code[..MaxLength] : code;
    }

    public static void Set(HttpContext httpContext, string code)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            Path = "/"
        };

        httpContext.Response.Cookies.Append(CookieName, code.Length > MaxLength ? code[..MaxLength] : code, options);
    }

    public static void Clear(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(CookieName);
    }
}
